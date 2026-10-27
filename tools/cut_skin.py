"""Cuts a sheet of 12 drawn pieces into the game's piece images.

The sheet has two rows of six pieces, King Queen Rook Bishop Knight Pawn, white on top. The light
background (a plain colour or a painted "transparent" checker) is removed from the outside in, up to
each piece's dark outline. Usage:

    python3 tools/cut_skin.py sheet.png unity/Assets/Resources/Skins/<skin id> [order]

order names the pieces in each row when the sheet is laid out differently, e.g. RNBKQBNR for a
back rank; a piece that appears twice is taken from its first column, and a missing one (such as
the pawn) is left to the game's recoloured piece.

Needs Pillow, numpy and scipy.
"""
import sys
from collections import deque
from PIL import Image
import numpy as np
src, out = sys.argv[1], sys.argv[2]
order = sys.argv[3] if len(sys.argv) > 3 else 'KQRBNP'
im = np.array(Image.open(src).convert('RGB')).astype(np.int32)
h, w, _ = im.shape
mx, mn = im.max(2), im.min(2)
lum = im.mean(2)
grey = (mx - mn) < 18
bgcand = grey & (mn > 200)
# Close small gaps in the dark outline so the fill cannot leak into a white piece.
from scipy.ndimage import binary_dilation as _dil
barrier = _dil(lum < 90, iterations=3)
bgcand = bgcand & ~barrier
# Flood fill background from the borders.
bg = np.zeros((h, w), bool)
q = deque()
for x in range(w):
    for y in (0, h-1):
        if bgcand[y, x] and not bg[y, x]: bg[y, x] = True; q.append((y, x))
for y in range(h):
    for x in (0, w-1):
        if bgcand[y, x] and not bg[y, x]: bg[y, x] = True; q.append((y, x))
while q:
    y, x = q.popleft()
    for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
        ny, nx = y+dy, x+dx
        if 0 <= ny < h and 0 <= nx < w and not bg[ny, nx] and bgcand[ny, nx]:
            bg[ny, nx] = True; q.append((ny, nx))
# Give back the background strip the barrier covered, without crossing the outline gaps again.
grow = (grey & (mn > 200)) & barrier
for _ in range(4):
    bg = bg | (_dil(bg) & grow)
alpha = np.where(bg, 0, 255).astype(np.float32)
rgb = im.astype(np.float32).copy()
# Soft edge: grey pixels next to the background fade out and turn black (the outline colour).
from scipy.ndimage import binary_dilation, label
near = binary_dilation(bg, iterations=3) & ~bg & grey
a = np.clip((235 - lum) / (235 - 30), 0, 1)
alpha[near] = (a[near] * 255)
rgb[near] = 0
rgba = np.dstack([rgb, alpha]).clip(0, 255).astype(np.uint8)
solid = alpha > 40
lab, n = label(solid)
sizes = np.bincount(lab.ravel()); sizes[0] = 0
blobs = [i for i in range(1, n+1) if sizes[i] > 2000]
boxes = []
for i in blobs:
    ys, xs = np.where(lab == i)
    boxes.append((xs.min(), ys.min(), xs.max(), ys.max(), i))
print('blobs', len(boxes))
# Merge blobs that belong to one piece (e.g. king's cross): group by column/row cell.
cells = {}
members = {}
for x0, y0, x1, y1, i in boxes:
    cx, cy = (x0+x1)/2, (y0+y1)/2
    col = int(cx / (w/len(order))); row = int(cy / (h/2))
    b = cells.get((row, col))
    cells[(row, col)] = (min(b[0],x0), min(b[1],y0), max(b[2],x1), max(b[3],y1)) if b else (x0, y0, x1, y1)
    members.setdefault((row, col), []).append(i)
print(sorted(cells))
maxh = max(b[3]-b[1] for b in cells.values()); maxw = max(b[2]-b[0] for b in cells.values())
scale = 232 / max(maxh, maxw)
for (row, col), (x0, y0, x1, y1) in cells.items():
    if order.index(order[col]) != col:
        continue  # a repeated piece
    # Keep only pixels of this cell so a neighbour's edge cannot sneak in.
    crop = rgba[y0:y1+1, x0:x1+1].copy()
    # Grown a little, so the soft edge around the piece stays.
    own = binary_dilation(np.isin(lab, members[(row, col)]), iterations=4)[y0:y1+1, x0:x1+1]
    crop[~own, 3] = 0
    piece = Image.fromarray(crop, 'RGBA')
    pw, ph = piece.size
    piece = piece.resize((max(1,round(pw*scale)), max(1,round(ph*scale))), Image.LANCZOS)
    canvas = Image.new('RGBA', (256, 256), (0,0,0,0))
    px = (256 - piece.size[0]) // 2
    py = 256 - 12 - piece.size[1]  # stand on a common baseline
    canvas.alpha_composite(piece, (px, py))
    canvas.save(f"{out}/{'w' if row==0 else 'b'}{order[col]}.png")
