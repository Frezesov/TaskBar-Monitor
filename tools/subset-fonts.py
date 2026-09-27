"""Builds the overlay fonts in Assets/Fonts from upstream static TTFs.

Sources (SIL OFL 1.1, no reserved font names):
  Montserrat      https://github.com/JulietaUla/Montserrat          fonts/ttf/Montserrat-{Regular,SemiBold,Bold}.ttf
  JetBrains Mono  https://github.com/JetBrains/JetBrainsMono        fonts/ttf/JetBrainsMono-{Regular,SemiBold,Bold}.ttf
  Roboto          https://github.com/googlefonts/roboto-3-classic  release zip, hinted/static/Roboto-{Regular,Medium,Bold}.ttf

The overlay only draws labels, digits and a few symbols, so the fonts are cut down to Latin, Cyrillic and
punctuation. Hinting is kept: the overlay renders with TextFormattingMode.Display. All name records are kept,
because WPF groups the weights of a family by the typographic family name (name ID 16).

Usage: pip install fonttools; python subset-fonts.py <folder with upstream TTFs> <Assets/Fonts>
"""
import sys
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

UNICODES = ("U+0020-007E,U+00A0-017F,U+0400-045F,U+0490-0491,U+2010-2027,U+2030,U+2039-203A,"
            "U+20AC,U+20BD,U+2116,U+2122,U+2190-2199,U+2212,U+2219,U+221E")

FILES = [
    "Montserrat-Regular", "Montserrat-SemiBold", "Montserrat-Bold",
    "JetBrainsMono-Regular", "JetBrainsMono-SemiBold", "JetBrainsMono-Bold",
    "Roboto-Regular", "Roboto-Medium", "Roboto-Bold",
]

# WPF matches faces by weight class and the overlay asks for 400, 600 and 700.
# Upstream JetBrains Mono statics carry design-space weights (472, 558), so SemiBold and Bold would both
# resolve to the 558 face. Roboto has no SemiBold: its Medium stands in, otherwise 600 would fall to Bold.
WEIGHT_FIXES = {"JetBrainsMono-SemiBold": 600, "JetBrainsMono-Bold": 700, "Roboto-Medium": 600}


def main(source: Path, target: Path) -> None:
    for name in FILES:
        options = subset.Options()
        options.layout_features += ["tnum", "lnum", "case"]
        options.name_IDs = ["*"]
        options.name_languages = ["*"]
        font = subset.load_font(str(source / f"{name}.ttf"), options)
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=subset.parse_unicodes(UNICODES))
        subsetter.subset(font)
        if name in WEIGHT_FIXES:
            font["OS/2"].usWeightClass = WEIGHT_FIXES[name]
        subset.save_font(font, str(target / f"{name}.ttf"), options)
        print(name, TTFont(str(target / f"{name}.ttf"))["OS/2"].usWeightClass)


if __name__ == "__main__":
    main(Path(sys.argv[1]), Path(sys.argv[2]))
