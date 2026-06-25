#!/usr/bin/env python3
"""Sestaví samostatnou varianta_2.html z šablony + assetů vytažených z elektron-navrh.html.

Maskot i fonty (Baloo 2, Nunito; latin + latin-ext kvůli češtině) se vkládají inline
jako data-URI, aby byl výsledek nezávislý na internetu a otevíratelný dvojklikem —
stejně jako varianta_1.html.
"""
import re, json, gzip, base64, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[3]      # D:\spc\couch
HERE = pathlib.Path(__file__).resolve().parent          # .../varianty
BUNDLE = ROOT / "elektron-navrh.html"
TEMPLATE = HERE / "varianta_2.template.html"
OUT = HERE / "varianta_2.html"

# uuid assetů v bundlu (image/png maskot + woff2 font subsety)
ASSETS = {
    "__MASCOT__":          "b9a349d1-1be6-4a4c-967f-1425618d2d5a",
    "__BALOO_LATIN__":     "4eb953f0-9e4c-41bc-afcb-f1cf22bb5f16",
    "__BALOO_LATINEXT__":  "76171ab4-807e-4b8c-a83e-f93043d17827",
    "__NUNITO_LATIN__":    "84e28867-7b8b-44c9-b7d6-72d244db68db",
    "__NUNITO_LATINEXT__": "b223127f-d48e-48d6-a3e7-e08eeb993536",
}


def load_manifest():
    html = BUNDLE.read_text(encoding="utf-8")
    raw = re.search(r'<script type="__bundler/manifest">(.*?)</script>', html, re.S).group(1)
    return json.loads(raw)


def data_uri(entry):
    raw = base64.b64decode(entry["data"])
    if entry.get("compressed"):
        raw = gzip.decompress(raw)
    return f"data:{entry['mime']};base64,{base64.b64encode(raw).decode()}"


def main():
    man = load_manifest()
    out = TEMPLATE.read_text(encoding="utf-8")
    for token, uuid in ASSETS.items():
        out = out.replace(token, data_uri(man[uuid]))
    if "data:" not in out or "__" in re.sub(r"__bundler|__resources", "", out):
        # sanity: zbyly nějaké nenahrazené __TOKEN__?
        leftover = sorted(set(re.findall(r"__[A-Z_]+__", out)))
        if leftover:
            raise SystemExit(f"Nenahrazené tokeny: {leftover}")
    OUT.write_text(out, encoding="utf-8")
    print(f"OK -> {OUT}  ({OUT.stat().st_size/1024:.0f} KB)")


if __name__ == "__main__":
    main()
