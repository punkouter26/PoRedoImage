"""Rebuild src/PoRedoImage.Client/wwwroot/fonts/radzen-icons.woff2.

Radzen draws its icons from Material Symbols ligatures and ships the whole 3.1MB font. This
keeps only the ligatures Radzen.Blazor can emit (every name in its DLL, CSS and JS that the
font defines) plus the names this app passes to Icon="...". Run it after upgrading Radzen or
adding a new Icon name:  python SCRIPTS/subset-radzen-icons.py   (needs: pip install fonttools brotli)
"""
import glob
import os
import re

from fontTools import subset
from fontTools.ttLib import TTFont

repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
version = re.search(r'Include="Radzen.Blazor" Version="([^"]+)"',
                    open(os.path.join(repo, 'Directory.Packages.props'), encoding='utf-8').read()).group(1)
pkg = os.path.join(os.path.expanduser('~'), '.nuget', 'packages', 'radzen.blazor', version)
src = os.path.join(pkg, 'staticwebassets', 'fonts', 'MaterialSymbolsOutlined.woff2')
out = os.path.join(repo, 'src', 'PoRedoImage.Client', 'wwwroot', 'fonts', 'radzen-icons.woff2')


def ligatures(font):
    """Yields (name, component glyphs, ligature glyph) for every ligature the font defines."""
    rev = {g: chr(c) for c, g in font.getBestCmap().items()}
    for lookup in font['GSUB'].table.LookupList.Lookup:
        for st in lookup.SubTable:
            st = getattr(st, 'ExtSubTable', st)
            for first, ligs in getattr(st, 'ligatures', {}).items():
                for lig in ligs:
                    comps = [first] + lig.Component
                    if all(g in rev for g in comps):
                        yield ''.join(rev[g] for g in comps), comps, lig.LigGlyph


candidates = set()
for dll in glob.glob(os.path.join(pkg, 'lib', '*', 'Radzen.Blazor.dll')):
    candidates |= {m.decode('utf-16-le') for m in re.findall(rb'(?:[a-z0-9_]\x00){3,40}', open(dll, 'rb').read())}
candidates |= set(re.findall(r'content:\s*"([a-z0-9_]+)"',
                             open(os.path.join(pkg, 'staticwebassets', 'css', 'default.css'), encoding='utf-8').read()))
candidates |= set(re.findall(r"['\"]([a-z0-9_]{3,40})['\"]",
                             open(os.path.join(pkg, 'staticwebassets', 'Radzen.Blazor.js'), encoding='utf-8').read()))
for razor in glob.glob(os.path.join(repo, 'src', 'PoRedoImage.Client', '**', '*.razor'), recursive=True):
    # The whole line, not just Icon="…": a ternary like Icon="@(busy ? "hourglass_top" : "delete")"
    # nests quotes, so the attribute value alone stops at the first inner one.
    for line in re.findall(r'\bIcon=.*', open(razor, encoding='utf-8').read()):
        candidates |= set(re.findall(r'[a-z0-9_]{3,40}', line))

font = TTFont(src)
wanted = {name for name, _, _ in ligatures(font) if name in candidates}
keep = set()
for name, comps, lig in ligatures(font):
    if name in wanted:
        keep.add(lig)
        keep.update(comps)

opts = subset.Options()
opts.flavor = 'woff2'
opts.layout_features = ['*']
opts.layout_closure = False  # closure would re-add every ligature: all their components are letters
opts.notdef_outline = True
sub = subset.Subsetter(opts)
sub.populate(glyphs=sorted(keep))
sub.subset(font)
os.makedirs(os.path.dirname(out), exist_ok=True)
font.save(out)

missing = wanted - {name for name, _, _ in ligatures(TTFont(out))}
assert not missing, f'ligatures lost in subsetting: {sorted(missing)}'
print(f'{len(wanted)} icons, {os.path.getsize(out) // 1024}KB (from {os.path.getsize(src) // 1024}KB) -> {out}')
