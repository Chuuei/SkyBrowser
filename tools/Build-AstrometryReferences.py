"""Regenerate synthetic forward-WCS reference values with Astropy/WCSLIB.

Run in an isolated environment with astropy==8.0.2. No user photographs or
metadata are read. The application test suite reads the checked-in JSON only.
"""
import json
from pathlib import Path

import astropy
from astropy.io.fits import Header
from astropy.wcs import WCS


def header(projection):
    return dict(NAXIS=2, NAXIS1=128, NAXIS2=96, CTYPE1="RA---"+projection,
                CTYPE2="DEC--"+projection, CRPIX1=64.5, CRPIX2=48.5,
                CRVAL1=359.9, CRVAL2=78.0, CD1_1=-0.01, CD1_2=0.003,
                CD2_1=0.002, CD2_2=0.009, RADESYS="ICRS")


cases = []


def add(name, values):
    wcs = WCS(Header(values), relax=True)
    pixels = [[0, 0], [127, 95], [13.125, 77.25], [63.5, 47.5]]
    world = wcs.all_pix2world(pixels, 0, ra_dec_order=True)
    cases.append(dict(name=name, header=values,
                      samples=[dict(x=p[0], y=p[1], ra=float(s[0]), dec=float(s[1]))
                               for p, s in zip(pixels, world)]))


for projection in ["TAN", "SIN", "STG", "ZEA", "ARC"]:
    add(projection, header(projection))
values = header("TAN-SIP")
values.update(A_ORDER=9, B_ORDER=9, A_2_0=2e-4, B_0_2=-3e-4,
              A_3_1=1e-9, B_1_2=2e-7, A_9_0=1e-20, B_0_9=-1e-20)
add("SIP ninth-order, mixed terms", values)
values = header("TPV")
for i in range(40):
    values[f"PV1_{i}"] = (1 if i == 1 else 0) + 0.001 / (i + 1)
    values[f"PV2_{i}"] = (1 if i == 1 else 0) - 0.002 / (i + 1)
add("TPV all terms", values)
for i in range(40):
    values = header("TPV")
    # WCSLIB initializes unspecified linear coefficients to zero once any PV is
    # supplied. State the registered identity coefficients explicitly in oracles;
    # omitted-term defaults are tested separately against the TPV convention.
    values.update(PV1_1=1., PV2_1=1.)
    values[f"PV1_{i}"] = (1 if i == 1 else 0) + 0.03
    values[f"PV2_{i}"] = (1 if i == 1 else 0) - 0.02
    add(f"TPV term {i}", values)
values = header("TAN")
values.update(CTYPE1="DEC--TAN", CTYPE2="RA---TAN", CRVAL1=78., CRVAL2=359.9,
              CD1_1=0.002, CD1_2=0.009, CD2_1=-0.01, CD2_2=0.003)
add("Swapped axes", values)
values = header("TAN-SIP")
values.update(CTYPE1="DEC--TAN-SIP", CTYPE2="RA---TAN-SIP", CRVAL1=78., CRVAL2=359.9,
              CD1_1=0.002, CD1_2=0.009, CD2_1=-0.01, CD2_2=0.003,
              A_ORDER=3, B_ORDER=3, A_2_0=2e-4, B_1_2=1e-7)
add("SIP swapped axes", values)
values = header("TAN")
for key in ["CD1_1", "CD1_2", "CD2_1", "CD2_2"]:
    values.pop(key)
values.update(CDELT1=-0.01, CDELT2=0.009, PC1_1=0.98, PC1_2=0.17, PC2_1=-0.21, PC2_2=0.99)
add("PC and CDELT", values)
values = header("TAN")
for key in ["CD1_1", "CD1_2", "CD2_1", "CD2_2"]:
    values.pop(key)
values.update(CDELT1=-0.01, CDELT2=0.009, CROTA2=31.)
add("CROTA unequal scales", values)
for dec in [89.999, -89.999, 90., -90.]:
    values = header("TAN")
    values["CRVAL2"] = dec
    add(f"Pole {dec}", values)
values = header("TAN")
values.update(CRVAL2=90., LONPOLE=0.)
add("Explicit north-pole default", values)

target = Path(__file__).resolve().parents[1] / "tests/SkyBrowser.Tests/Data/astrometry-reference.json"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(dict(generator=f"Astropy {astropy.__version__} / WCSLIB", cases=cases), indent=2)+"\n", encoding="utf-8")
print(f"Wrote {len(cases)} independent reference cases.")
