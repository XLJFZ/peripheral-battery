"""Synchronize the standalone WebHID page for GitHub Pages."""
from pathlib import Path

root = Path(__file__).resolve().parent
target = root / 'docs'
target.mkdir(exist_ok=True)
(target / 'index.html').write_bytes((root / 'web' / 'index.html').read_bytes())
(target / '.nojekyll').touch()
