# Turns the demo frames the render tool writes (demo-<language>-<theme>/ with durations.txt)
# into GIFs: python make_gif.py <render folder> <gif folder>. Needs Pillow.
import os
import sys
from PIL import Image

root = sys.argv[1]
out = sys.argv[2]
os.makedirs(out, exist_ok=True)
for name in sorted(os.listdir(root)):
    folder = os.path.join(root, name)
    if not (name.startswith('demo-') and os.path.isdir(folder)):
        continue
    durations = [int(line) for line in open(os.path.join(folder, 'durations.txt')) if line.strip()]
    frames = []
    for i in range(len(durations)):
        image = Image.open(os.path.join(folder, f'{i:03}.png')).convert('RGB')
        frames.append(image.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE))
    target = os.path.join(out, name + '.gif')
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True, disposal=1)
    print(name, len(frames), 'frames', round(os.path.getsize(target) / 1024), 'KB', sum(durations) / 1000, 's')
