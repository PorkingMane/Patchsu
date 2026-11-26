#!/usr/bin/env python3
"""Replace or add per-file sections of a patch, keeping every other section byte for byte.
Usage: merge_sections.py <original.patch> <full new diff> <output.patch> <path> [<path>...]
Each <path> section is taken from the full new diff (replacing the original's, or inserted in path order)."""
import re, sys

orig, full, out, *paths = sys.argv[1:]


def split(text):
    i = text.find('diff --git ')
    if i < 0:
        return text, []
    return text[:i], [p for p in re.split(r'(?m)^(?=diff --git )', text[i:]) if p]


def key(section):
    return section.split('\n', 1)[0].split(' b/', 1)[1].rstrip('\r')


head, original = split(open(orig, newline='').read())
_, new = split(open(full, newline='').read())
new_map = {key(s): s for s in new}

missing = [p for p in paths if p not in new_map]
if missing:
    sys.exit(f'not in the new diff: {missing}')

result = [new_map[key(s)] if key(s) in paths else s for s in original]
present = {key(s) for s in result}

for p in paths:
    if p not in present:
        index = next((i for i, s in enumerate(result) if key(s) > p), len(result))
        result.insert(index, new_map[p])

open(out, 'w', newline='').write(head + ''.join(result))
print(f'{out}: {len(original)} -> {len(result)} sections')
