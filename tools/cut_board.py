"""Cuts a drawn chess board into square pictures for the game.

The game takes a few different light and dark squares from a board and repeats them, so the
board looks drawn without storing all 64. Usage:

    python3 tools/cut_board.py board.png unity/Assets/Resources/Boards/<board id> x0 y0 x1 y1 [count]

x0 y0 x1 y1 are the corners of the 8x8 squares inside the frame (in pixels of board.png, a1 at
the bottom left); count is how many squares of each colour to keep (8 by default). Each square is
cut a little inside its edges so the grid lines between squares stay out.

Needs Pillow.
"""
import os
import sys
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
x0, y0, x1, y1 = (float(v) for v in sys.argv[3:7])
count = int(sys.argv[7]) if len(sys.argv) > 7 else 8
im = Image.open(src).convert('RGB')
cw, ch = (x1 - x0) / 8, (y1 - y0) / 8
inset = 0.09
os.makedirs(out, exist_ok=True)
saved = {'light': 0, 'dark': 0}
# Spread the picks over the board: walk the squares in a scattered order.
for n in range(64):
    i = (n * 37) % 64
    file, row = i % 8, i // 8
    rank = 7 - row
    kind = 'dark' if (file + rank) % 2 == 0 else 'light'
    if saved[kind] >= count:
        continue
    left = x0 + (file + inset) * cw
    top = y0 + (row + inset) * ch
    box = (round(left), round(top), round(left + cw * (1 - 2 * inset)), round(top + ch * (1 - 2 * inset)))
    im.crop(box).resize((128, 128), Image.LANCZOS).save(f'{out}/{kind}{saved[kind]}.png')
    saved[kind] += 1
print(saved)
