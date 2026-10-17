"""Rebuild the offline NASA/Hubble distance snapshot; no app-time network access.

Only numerical Distance facts are retained, never article text or imagery.
Cached HTML and per-page SHA-256 hashes make the snapshot auditable.
"""

import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import date
import hashlib
import html
import json
from pathlib import Path
import re
import urllib.request
from TargetDistanceSources import expand_catalogue

BASE = "https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-messier-catalog/"
EXCLUDED = {24: "Line-of-sight star cloud; no single Distance fact in the NASA table."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--as-of", required=True, type=date.fromisoformat)
    parser.add_argument("--refresh", action="store_true")
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    cache = root / "artifacts/distance-sources"
    cache.mkdir(parents=True, exist_ok=True)

    def read(url, name, extension=".html", limit=4_000_000, timeout=60):
        path = cache / (name + extension)
        if not path.exists() or args.refresh:
            if args.offline:
                raise ValueError(f"Missing cached source: {name}")
            request = urllib.request.Request(url, headers={"User-Agent": "SkyBrowser distance catalogue builder"})
            with urllib.request.urlopen(request, timeout=timeout) as response:
                data = response.read(limit + 1)
            if len(data) > limit:
                raise ValueError(f"Source page too large: {name}")
            path.write_bytes(data)
        return path.read_bytes()

    index = read(BASE, "index")
    urls = sorted(set(re.findall(re.escape(BASE) + r"messier-\d+/", index.decode("utf-8"))),
                  key=lambda url: int(re.search(r"messier-(\d+)", url)[1]))
    if len(urls) < 80:
        raise ValueError("The NASA index no longer contains the expected Messier links; review the source.")

    def extract(url):
        number = int(re.search(r"messier-(\d+)", url)[1])
        data = read(url, f"messier-{number}")
        if number in EXCLUDED:
            return None
        match = re.search(r">Distance</h2>\s*<p[^>]*>(.*?)</p>", data.decode("utf-8"), re.S)
        if match is None:
            raise ValueError(f"No Distance fact for M{number}; review the source.")
        fact = " ".join(html.unescape(re.sub(r"<[^>]+>", "", match[1])).split())
        value = re.fullmatch(r"(?:About |~)?([\d,]+(?:\.\d+)?) (million |thousand )?light-years", fact)
        if value is None:
            raise ValueError(f"Ambiguous Distance fact for M{number}: {fact!r}; review before inclusion.")
        scale = {None: 1, "thousand ": 1000, "million ": 1_000_000}[value[2]]
        light_years = float(value[1].replace(",", "")) * scale
        if not 0 < light_years < 1e10:
            raise ValueError(f"Invalid distance for M{number}")
        return {"designation": f"M{number}", "lightYears": light_years, "sourceValue": fact,
                "sourceUrl": url, "sourceSha256": hashlib.sha256(data).hexdigest()}

    with ThreadPoolExecutor(max_workers=4) as workers:
        futures = [workers.submit(extract, url) for url in urls]
        entries, errors = [], []
        for future in futures:
            try:
                entry = future.result()
                if entry is not None:
                    entries.append(entry)
            except Exception as error:
                errors.append(str(error))
        if errors:
            raise ValueError("Source review needed:\n" + "\n".join(errors))
    catalogue = json.loads((root / "src/SkyBrowser.Core/Data/deep-sky.json").read_text(encoding="utf-8"))["Targets"]
    entries, audit = expand_catalogue(entries, catalogue, read)
    document = {"version": 2, "asOf": args.as_of.isoformat(), "source": "NASA / Hubble and SIMBAD / CDS literature distances",
                "sourceIndex": BASE, "sourceIndexSha256": hashlib.sha256(index).hexdigest(),
                "excluded": {f"M{number}": reason for number, reason in EXCLUDED.items()}, "audit": audit, "targets": entries}
    destination = root / "src/SkyBrowser.Core/Data/target-distances.json"
    destination.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\r\n")
    print(f"Wrote {len(entries)} sourced distance estimates.")
    print(json.dumps(audit["coverage"], indent=2))
    print(json.dumps(audit["rejected"], indent=2))


if __name__ == "__main__":
    main()
