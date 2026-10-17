"""Validated source ingestion for the offline distance build (Python standard library)."""

import base64
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import hashlib
import html
import math
import re
import struct
import urllib.parse
import xml.etree.ElementTree as ET

PARSEC_LIGHT_YEARS = 3.261563777167433
CALDWELL = "https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-caldwell-catalog/"
SIMBAD_QUERY = """SELECT TOP 200000 i.id AS designation,b.main_id,b.ra,b.dec,b.otype,d.dist,d.unit,d.qual,d.method,d.bibcode,d.minus_err,d.plus_err
FROM mesDistance AS d JOIN basic AS b ON d.oidref=b.oid JOIN ident AS i ON i.oidref=b.oid
WHERE (i.id LIKE 'NGC %' OR i.id LIKE 'IC %') AND b.otype IN ('G','GiC','GiG','GiP','PaG','ClG','AGN','Sy1','Sy2','SyG','LIN','H2G','SBG','LSB','EmG','BiC','GlC','OpC','Cl*','As*','PN','HII','RNe','DNe','SNR','Neb','ISM','MoC','Bub')"""
INDEPENDENT_METHODS = {"t-f", "st-l", "sbf", "cep", "ceph", "cepheid", "t-rgb", "t-rdb", "trgb",
                       "paral", "plx", "pm", "rrlyr", "pnlf", "sn", "bs", "sed-fit"}
GALACTIC_TYPES = {"GlC", "OpC", "Cl*", "As*", "PN", "HII", "RNe", "DNe", "SNR", "Neb", "ISM", "MoC", "Bub"}


def identifier(value):
    match = re.fullmatch(r"\s*(NGC|IC|M|C)\s*0*(\d+)([A-Z]?)\s*", value, re.I)
    return f"{match[1].upper()}{int(match[2])}{match[3].upper()}" if match else None


def parse_nasa_fact(page):
    match = re.search(r">Distance</h2>\s*<p[^>]*>(.*?)</p>", page, re.S)
    if match is None:
        return None
    fact = " ".join(html.unescape(re.sub(r"<[^>]+>", "", match[1])).split())
    value = re.fullmatch(r"(?:About |~)?([\d,]+(?:\.\d+)?) (million |thousand )?light-years", fact)
    if value is None:
        return None  # Ranges, lower limits and multiple values are not single-object estimates.
    scale = {None: 1, "thousand ": 1000, "million ": 1_000_000}[value[2]]
    distance = float(value[1].replace(",", "")) * scale
    return (distance, fact) if math.isfinite(distance) and 0 < distance < 1e10 else None


def votable_rows(data):
    root = ET.fromstring(data)
    statuses = [item.attrib.get("value") for item in root.findall(".//{*}INFO") if item.attrib.get("name") == "QUERY_STATUS"]
    if not statuses or any(status != "OK" for status in statuses):
        raise ValueError(f"Incomplete or failed SIMBAD query: {statuses}")
    fields = root.findall(".//{*}FIELD")
    names = [field.attrib["name"] for field in fields]
    table = root.find(".//{*}TABLEDATA")
    if table is not None:
        rows = [dict(zip(names, [cell.text or "" for cell in row])) for row in table]
    else:
        stream = root.find(".//{*}BINARY/{*}STREAM")
        if stream is None or stream.attrib.get("encoding") != "base64":
            raise ValueError("Unsupported SIMBAD result serialization")
        binary, offset, rows = base64.b64decode(stream.text), 0, []
        while offset < len(binary):
            values = []
            for field in fields:
                if field.attrib["datatype"] == "char":
                    count = field.attrib["arraysize"]
                    if count == "*":
                        count = struct.unpack_from(">i", binary, offset)[0]
                        offset += 4
                    else:
                        count = int(count)
                    if count < 0 or offset + count > len(binary):
                        raise ValueError("Truncated SIMBAD string")
                    values.append(binary[offset:offset + count].decode("utf-8").strip("\x00 "))
                    offset += count
                elif field.attrib["datatype"] == "double":
                    values.append(struct.unpack_from(">d", binary, offset)[0])
                    offset += 8
                else:
                    raise ValueError("Unexpected SIMBAD column type")
            rows.append(dict(zip(names, values)))
    if len(rows) >= 200000:
        raise ValueError("SIMBAD query reached its row cap; do not publish a truncated snapshot")
    return rows


def number(value):
    try:
        parsed = float(value)
        return parsed if math.isfinite(parsed) else None
    except (ValueError, TypeError):
        return None


def eligible_measurement(row):
    distance = number(row["dist"])
    scale = {"pc": 1, "kpc": 1000, "Mpc": 1_000_000}.get(row["unit"])
    method = row["method"].strip().lower()
    if distance is None or distance <= 0 or scale is None or row["qual"].strip("\x00 "):
        return None
    if method not in INDEPENDENT_METHODS:
        # Unspecified methods are only accepted for nearby, non-galaxy objects.
        # Redshift, flow and kinematic methods are excluded, not converted to distances.
        if method or row["otype"] not in GALACTIC_TYPES or distance * scale > 150000:
            return None
    if not re.fullmatch(r"\d{4}[A-Za-z0-9.&+_/-]{10,25}", row["bibcode"]) or not 1800 <= int(row["bibcode"][:4]) <= 2100:
        return None
    light_years = distance * scale * PARSEC_LIGHT_YEARS
    if not 0 < light_years < 1e10:
        return None
    errors = []
    for name in ("minus_err", "plus_err"):
        error = number(row[name])
        error = abs(error) if error is not None and error != 0 else None
        if error is not None and error >= distance:
            return None
        errors.append(error * scale * PARSEC_LIGHT_YEARS if error is not None else None)
    return light_years, errors


def measurement_rank(measurement):
    row, (distance, errors) = measurement
    relative = max(error for error in errors if error is not None) / distance if any(error is not None for error in errors) else math.inf
    return (-int(row["bibcode"][:4]), relative, row["bibcode"], row["method"], row["unit"], float(row["dist"]), row["designation"])


def angular_separation(first, second):
    ra1, dec1, ra2, dec2 = map(math.radians, [first[0], first[1], second[0], second[1]])
    square = math.sin((dec2-dec1)/2)**2 + math.cos(dec1)*math.cos(dec2)*math.sin((ra2-ra1)/2)**2
    return math.degrees(2 * math.asin(math.sqrt(min(1, max(0, square)))))


def expand_catalogue(nasa_entries, catalogue, read):
    aliases = {}
    for target in catalogue:
        for alias in target["Designations"] + [target["Id"]]:
            key = identifier(alias)
            if key:
                if key in aliases and aliases[key]["Id"] != target["Id"]:
                    raise ValueError(f"Ambiguous catalogue alias {key}")
                aliases[key] = target
    selected, rejected = {}, Counter()

    def add_nasa(entry, name):
        target = aliases.get(identifier(entry["designation"]))
        if target is None:
            raise ValueError(f"NASA object missing from position catalogue: {entry['designation']}")
        item = dict(entry, designation=target["Id"], sourceDesignation=entry["designation"], sourceName=name,
                    sourceKind="NASA", method="Published rounded estimate", reference="",
                    minusErrorLightYears=None, plusErrorLightYears=None)
        selected.setdefault(target["Id"], item)

    for entry in nasa_entries:
        add_nasa(entry, "NASA / Hubble's Messier Catalog")

    index = read(CALDWELL, "caldwell-index")
    urls = sorted(set(re.findall(re.escape(CALDWELL) + r"caldwell-\d+(?:-\d+)?/", index.decode("utf-8"))))
    if len(urls) < 80:
        raise ValueError("The NASA Caldwell source index needs review")

    def caldwell(url):
        suffix = re.search(r"caldwell-(\d+(?:-\d+)?)/", url)[1]
        designations = ["C" + number for number in suffix.split("-")]
        data = read(url, "caldwell-" + suffix)
        fact = parse_nasa_fact(data.decode("utf-8"))
        if fact is None:
            return []
        return [{"designation": designation, "lightYears": fact[0], "sourceValue": fact[1],
                 "sourceUrl": url, "sourceSha256": hashlib.sha256(data).hexdigest()} for designation in designations]

    with ThreadPoolExecutor(max_workers=4) as workers:
        for items in workers.map(caldwell, urls):
            if not items:
                rejected["NASA Caldwell: no single numerical distance"] += 1
            else:
                for item in items:
                    add_nasa(item, "NASA / Hubble's Caldwell Catalog")

    # Explicitly reviewed single-object facts for Caldwell entries absent from the
    # Hubble image index. Never select the first number or borrow a neighbour's value.
    supplements = [
        ("C41", 150, "https://apod.nasa.gov/apod/ap200122.html", r"Hyades", r"150\s+light[- ]years"),
        ("IC405", 1500, "https://apod.nasa.gov/apod/ap161201.html", r"IC\s*405", r"1,500\s+light[- ]years"),
        ("NGC2237", 5000, "https://apod.nasa.gov/apod/ap250617.html", r"NGC\s*2237", r"5,000\s+light[- ]years")
    ]
    for designation, distance, url, name_pattern, value_pattern in supplements:
        page = read(url, "apod-" + designation)
        text = " ".join(html.unescape(re.sub(r"<[^>]+>", " ", page.decode("utf-8"))).split())
        if not re.search(name_pattern, text, re.I) or not re.search(value_pattern, text, re.I):
            raise ValueError(f"Reviewed NASA supplementary fact changed for {designation}")
        add_nasa({"designation": designation, "lightYears": distance,
                  "sourceValue": f"{distance:,} light-years", "sourceUrl": url,
                  "sourceSha256": hashlib.sha256(page).hexdigest()}, "NASA / APOD")
        target_id = aliases[identifier(designation)]["Id"]
        if selected[target_id]["sourceUrl"] == url:
            selected[target_id]["sourceKind"] = "NASA_APOD"

    query_url = "https://simbad.cds.unistra.fr/simbad/sim-tap/sync?" + urllib.parse.urlencode(
        {"request": "doQuery", "lang": "adql", "format": "votable", "maxrec": "200001", "query": SIMBAD_QUERY})
    data = read(query_url, "simbad-distances", extension=".xml", limit=50_000_000, timeout=240)
    rows = votable_rows(data)
    digest, candidates = hashlib.sha256(data).hexdigest(), defaultdict(list)
    for row in rows:
        key = identifier(row["designation"])
        target = aliases.get(key)
        if target is None:
            rejected["SIMBAD: component/member or unsupported identity"] += 1
            continue
        value = eligible_measurement(row)
        if value is None:
            rejected["SIMBAD: unsupported method/quality/unit/value/error/reference"] += 1
            continue
        if row["otype"] in GALACTIC_TYPES and target["TypeCode"].startswith("G") and target["TypeCode"] != "GCl":
            rejected["SIMBAD: incompatible object type"] += 1
            continue
        position = target["Position"]
        source_position = (number(row["ra"]), number(row["dec"]))
        extent = max(target["MajorArcMinutes"] or 0, target["MinorArcMinutes"] or 0)
        # Published cluster/nebula centres can differ within the object's full extent.
        # Exact catalogue identity is mandatory; position is an additional bounded check.
        if None in source_position or angular_separation((position["RightAscension"], position["Declination"]), source_position) > max(0.02, min(1, extent / 60)):
            rejected["SIMBAD: identity/position disagreement"] += 1
            continue
        candidates[target["Id"]].append((row, value))

    for target_id, measurements in candidates.items():
        if target_id in selected:
            continue

        row, (distance, errors) = min(measurements, key=measurement_rank)
        selected[target_id] = {
            "designation": target_id, "lightYears": distance,
            "sourceValue": f"{float(row['dist']):g} {row['unit']} (≈{distance:,.0f} light-years)",
            "sourceDesignation": identifier(row["designation"]), "sourceName": "SIMBAD / CDS",
            "sourceKind": "SIMBAD", "sourceUrl": "https://simbad.cds.unistra.fr/simbad/sim-id?" + urllib.parse.urlencode({"Ident": identifier(row["designation"])}),
            "sourceSha256": digest, "method": row["method"] or "Literature distance (method unspecified)",
            "reference": row["bibcode"], "minusErrorLightYears": errors[0], "plusErrorLightYears": errors[1]
        }
    coverage = {}
    for name, prefix in [("Messier", "M"), ("Caldwell", "C"), ("NGC", "NGC"), ("IC", "IC")]:
        targets = [target for target in catalogue if any(
            alias.startswith(prefix) if prefix in {"NGC", "IC"} else re.fullmatch(prefix + r"\d+[A-Z]?", alias)
            for alias in target["Designations"])]
        coverage[name] = {"available": sum(target["Id"] in selected for target in targets), "total": len(targets)}
    audit = {"selection": "NASA Messier, NASA Caldwell, reviewed NASA APOD supplements, then latest eligible SIMBAD literature value; ties use reported fractional error and stable bibliographic order.",
             "simbadQuery": SIMBAD_QUERY, "simbadSourceSha256": digest, "coverage": coverage,
             "rejected": dict(sorted(rejected.items()))}
    return sorted(selected.values(), key=lambda item: item["designation"]), audit
