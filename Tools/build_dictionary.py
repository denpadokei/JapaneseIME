"""Builds the compact kana -> kanji candidate table shipped with the plugin.

Source data: JMdict (EDRDG), CC BY-SA 4.0. Only readings and kanji forms are
used; glosses are dropped.

Usage:
    python Tools/build_dictionary.py <JMdict_e.gz> <output.txt.gz>
"""

import gzip
import sys
import xml.etree.ElementTree as ET

MAX_CANDIDATES = 6

PRIORITY = {
    "ichi1": 10,
    "news1": 9,
    "spec1": 8,
    "gai1": 7,
    "news2": 6,
    "ichi2": 5,
    "spec2": 4,
    "gai2": 3,
    "nf01": 2,
    "nf02": 2,
    "nf03": 2,
    "nf04": 2,
    "nf05": 2,
    "nf06": 1,
    "nf07": 1,
    "nf08": 1,
    "nf09": 1,
    "nf10": 1,
}


def kata_to_hira(text):
    return "".join(chr(ord(c) - 0x60) if 0x30A1 <= ord(c) <= 0x30F6 else c for c in text)


def has_kanji(text):
    return any(
        0x4E00 <= ord(c) <= 0x9FFF or 0x3400 <= ord(c) <= 0x4DBF or c == "\u3005"
        for c in text
    )


def score(priorities):
    return sum(PRIORITY.get(p, 0) for p in priorities)


def build(source, destination):
    readings = {}

    with gzip.open(source, "rb") as handle:
        for event, element in ET.iterparse(handle, events=("end",)):
            if element.tag != "entry":
                continue

            rebs = [e.text for e in element.findall("r_ele/reb") if e.text]
            kebs = [e.text for e in element.findall("k_ele/keb") if e.text]
            priorities = [
                e.text
                for e in element.findall("k_ele/ke_pri")
                + element.findall("r_ele/re_pri")
                if e.text
            ]
            entry_score = score(priorities)

            if kebs and entry_score > 0:
                for reading in rebs:
                    hiragana = kata_to_hira(reading)
                    if not hiragana or has_kanji(hiragana):
                        continue

                    bucket = readings.setdefault(hiragana, {})
                    for index, keb in enumerate(kebs):
                        if not has_kanji(keb):
                            continue

                        weight = entry_score * 10 - index
                        if keb not in bucket or weight > bucket[keb]:
                            bucket[keb] = weight

            element.clear()

    lines = []
    for reading in sorted(readings):
        candidates = [
            k
            for k, _ in sorted(readings[reading].items(), key=lambda item: (-item[1], item[0]))
        ][:MAX_CANDIDATES]
        if candidates:
            lines.append(reading + "\t" + "\t".join(candidates))

    payload = ("\n".join(lines) + "\n").encode("utf-8")
    with open(destination, "wb") as raw:
        with gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as handle:
            handle.write(payload)

    print("readings:", len(lines))
    print("lines bytes:", len(payload))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        raise SystemExit(1)

    build(sys.argv[1], sys.argv[2])

