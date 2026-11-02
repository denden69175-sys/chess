"""Sizes the pieces of a drawn set like the classic pieces, which look right on the board.

Each piece (wK.png ... bP.png, 256x256) is scaled so it is as tall as the classic piece of the same
kind in unity/Assets/Resources/Pieces, stands on the same baseline and is centred; a piece that
would get wider than the picture is kept narrower. Usage:

    python3 tools/fit_skin.py unity/Assets/Resources/Skins/<skin id> [...]

Needs Pillow and numpy.
"""
import os
import sys
import numpy as np
from PIL import Image

CLASSIC = os.path.join(os.path.dirname(__file__), '..', 'unity', 'Assets', 'Resources', 'Pieces')
SIZE, MAX_WIDTH = 256, 240


def bbox(image):
    alpha = np.asarray(image)[:, :, 3]
    ys, xs = np.where(alpha > 40)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


for folder in sys.argv[1:]:
    for name in sorted(os.listdir(folder)):
        if not name.endswith('.png'):
            continue
        classic = Image.open(os.path.join(CLASSIC, name)).convert('RGBA')
        _, ctop, _, cbottom = bbox(classic)
        piece = Image.open(os.path.join(folder, name)).convert('RGBA')
        x0, y0, x1, y1 = bbox(piece)
        piece = piece.crop((x0, y0, x1, y1))
        scale = min((cbottom - ctop) / piece.height, MAX_WIDTH / piece.width)
        piece = piece.resize((round(piece.width * scale), round(piece.height * scale)), Image.LANCZOS)
        canvas = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
        canvas.alpha_composite(piece, ((SIZE - piece.width) // 2, cbottom - piece.height))
        canvas.save(os.path.join(folder, name))
    print('fitted', folder)
