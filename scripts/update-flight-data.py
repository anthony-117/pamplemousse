"""Rebuilds the embedded flight reference data from Travelpayouts' public lists (no token needed).

Run from the repo root: python scripts/update-flight-data.py
"""
import gzip
import json
import urllib.request
from pathlib import Path

BASE = "https://api.travelpayouts.com/data/en"
OUT = Path("src/Pamplemousse.Infrastructure/ReferenceData/Data")


def fetch(name):
    with urllib.request.urlopen(f"{BASE}/{name}.json", timeout=60) as response:
        return json.load(response)


def write(name, lines):
    # mtime=0 keeps the file byte-identical when the data has not changed.
    with open(OUT / name, "wb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as gz:
        gz.write("\n".join(lines).encode("utf-8"))


def clean(text):
    return " ".join(text.split())


# Only real airports count: the lists also flag some railway and bus stations as flightable.
airports = fetch("airports")
served = {a["city_code"] for a in airports if a.get("flightable") and a.get("iata_type") == "airport" and a.get("city_code")}

cities = [
    "\t".join([c["code"], clean(c["name"]), c["country_code"], str(c["coordinates"]["lat"]), str(c["coordinates"]["lon"])])
    for c in fetch("cities")
    if c["code"] in served and c.get("coordinates") and c.get("name")
]
write("flight-cities.tsv.gz", sorted(cities))

airlines = [f"{a['code']}\t{clean(a['name'])}" for a in fetch("airlines") if a.get("code") and a.get("name")]
write("airlines.tsv.gz", sorted(airlines))

print(f"{len(cities)} flight cities, {len(airlines)} airlines")
