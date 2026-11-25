"""Builds Curvy's .ico files from the SVG sources in src/Curvy.App/Assets/Icon.

    python tools/icons/build_icons.py

Needs Microsoft Edge (renders the SVGs headless, nothing is shown) and Pillow (packs the .ico).
Each SVG is rendered once at 1024 px and downscaled, so small sizes are properly antialiased;
the app icon uses curvy-small.svg for 32 px and below and curvy.svg above that.

Outputs (src/Curvy.App/Assets):
  curvy.ico                       app icon: exe, window, taskbar (16..256)
  tray-light.ico / tray-dark.ico  tray glyph for a light / dark taskbar (dark / white line)
  tray-light-off.ico / tray-dark-off.ico   same, dimmed, while the EQ is off
"""

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src" / "Curvy.App" / "Assets"
SOURCES = ASSETS / "Icon"
RENDER = 1024

APP_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SMALL_MAX = 32  # sizes up to this use curvy-small.svg
TRAY_SIZES = [16, 20, 24, 32, 40, 48]
OFF_OPACITY = 0.4

EDGE_CANDIDATES = [
    Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft/Edge/Application/msedge.exe",
    Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Microsoft/Edge/Application/msedge.exe",
]


def find_edge() -> Path:
    for p in EDGE_CANDIDATES:
        if p.exists():
            return p
    sys.exit("Microsoft Edge not found; it is used to render the SVGs.")


def render(edge: Path, svg: Path, work: Path, color: str = "#000000") -> Image.Image:
    """Renders an SVG to a transparent RENDER x RENDER RGBA image. `color` feeds currentColor."""
    svg_text = svg.read_text(encoding="utf-8")
    html = work / (svg.stem + "-" + color.strip("#") + ".html")
    html.write_text(
        "<!doctype html><html><head><meta charset='utf-8'><style>"
        f"html,body{{margin:0;padding:0;background:transparent;overflow:hidden}}"
        f"svg{{display:block;width:{RENDER}px;height:{RENDER}px;color:{color}}}"
        "</style></head><body>" + svg_text + "</body></html>",
        encoding="utf-8",
    )
    png = html.with_suffix(".png")
    subprocess.run(
        [
            str(edge), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
            f"--user-data-dir={work / 'edge-profile'}",
            "--force-device-scale-factor=1", "--default-background-color=00000000",
            f"--window-size={RENDER},{RENDER}", f"--screenshot={png}", html.as_uri(),
        ],
        check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120,
    )
    img = Image.open(png).convert("RGBA")
    if img.size != (RENDER, RENDER):
        img = img.crop((0, 0, RENDER, RENDER))
    if img.getpixel((0, 0))[3] != 0:
        sys.exit(f"{svg.name}: render has no transparent background (Edge ignored the background flag).")
    return img


def scaled(img: Image.Image, size: int) -> Image.Image:
    return img.resize((size, size), Image.Resampling.LANCZOS)


def dimmed(img: Image.Image, opacity: float) -> Image.Image:
    r, g, b, a = img.split()
    return Image.merge("RGBA", (r, g, b, a.point(lambda v: round(v * opacity))))


def save_ico(path: Path, frames: list[Image.Image]) -> None:
    frames = sorted(frames, key=lambda f: f.width, reverse=True)
    frames[0].save(path, format="ICO", sizes=[f.size for f in frames], append_images=frames[1:])
    with Image.open(path) as check:
        got = sorted(s[0] for s in check.info["sizes"])
    want = sorted(f.width for f in frames)
    if got != want:
        sys.exit(f"{path.name}: expected sizes {want}, got {got}")
    print(f"wrote {path.relative_to(ROOT)}  ({', '.join(map(str, want))})")


def main() -> None:
    edge = find_edge()
    work = Path(tempfile.mkdtemp(prefix="curvy-icons-"))
    try:
        large = render(edge, SOURCES / "curvy.svg", work)
        small = render(edge, SOURCES / "curvy-small.svg", work)
        save_ico(ASSETS / "curvy.ico",
                 [scaled(small if s <= SMALL_MAX else large, s) for s in APP_SIZES])

        tray = SOURCES / "curvy-tray.svg"
        for name, color in (("tray-dark", "#FFFFFF"), ("tray-light", "#1A1A1A")):
            glyph = render(edge, tray, work, color)
            save_ico(ASSETS / f"{name}.ico", [scaled(glyph, s) for s in TRAY_SIZES])
            save_ico(ASSETS / f"{name}-off.ico", [dimmed(scaled(glyph, s), OFF_OPACITY) for s in TRAY_SIZES])
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
