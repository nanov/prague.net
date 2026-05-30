"""Local Excalidraw render helper.

Workaround for the global skill's renderer: headless Chromium blocks
ES-module imports from https:// when the page is served via file://, so
we serve the skill's render_template.html over local HTTP instead.

Usage (uses the skill's venv that already has playwright installed):
    ~/.claude/skills/excalidraw-diagram/references/.venv/bin/python \
        www/docs/images/_render.py www/docs/images/data-flow.excalidraw
"""

from __future__ import annotations

import functools
import http.server
import json
import socketserver
import sys
import threading
from pathlib import Path


SKILL_REF = Path.home() / ".claude/skills/excalidraw-diagram/references"
# Project-local template pinned to a known-good Excalidraw version (the
# global skill's template uses @latest which currently 404s on a sub-dep).
TEMPLATE = Path(__file__).parent / "_template.html"


def main() -> None:
	if len(sys.argv) < 2:
		print("usage: _render.py <path-to-file.excalidraw>", file=sys.stderr)
		sys.exit(2)

	src = Path(sys.argv[1]).resolve()
	dst = src.with_suffix(".png")

	data = json.loads(src.read_text(encoding="utf-8"))
	elements = [e for e in data.get("elements", []) if not e.get("isDeleted")]
	if not elements:
		print("ERROR: no elements to render", file=sys.stderr)
		sys.exit(1)

	# Bounding box → viewport size
	xs = []
	ys = []
	for el in elements:
		x = el.get("x", 0)
		y = el.get("y", 0)
		w = el.get("width", 0)
		h = el.get("height", 0)
		if el.get("type") in ("arrow", "line") and "points" in el:
			for px, py in el["points"]:
				xs.append(x + px)
				ys.append(y + py)
		else:
			xs.extend([x, x + abs(w)])
			ys.extend([y, y + abs(h)])
	pad = 80
	vp_w = min(int(max(xs) - min(xs) + pad * 2), 2400)
	vp_h = max(int(max(ys) - min(ys) + pad * 2), 600)

	# Serve the local template directory over HTTP so https:// module imports work.
	handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(TEMPLATE.parent))
	server = socketserver.TCPServer(("127.0.0.1", 0), handler)
	port = server.server_address[1]
	threading.Thread(target=server.serve_forever, daemon=True).start()
	url = f"http://127.0.0.1:{port}/{TEMPLATE.name}"

	from playwright.sync_api import sync_playwright

	with sync_playwright() as p:
		browser = p.chromium.launch(headless=True)
		page = browser.new_page(viewport={"width": vp_w, "height": vp_h}, device_scale_factor=2)
		page.on("console", lambda msg: print(f"[browser:{msg.type}] {msg.text}", file=sys.stderr))
		page.on("pageerror", lambda err: print(f"[browser:error] {err}", file=sys.stderr))
		page.on("requestfailed", lambda req: print(f"[browser:reqfail] {req.url} -- {req.failure}", file=sys.stderr))
		page.goto(url)
		try:
			page.wait_for_function("window.__moduleReady === true", timeout=60000)
		except Exception as e:
			print(f"[diag] moduleReady wait failed: {e}", file=sys.stderr)
			print(f"[diag] page.url = {page.url}", file=sys.stderr)
			raise
		result = page.evaluate(f"window.renderDiagram({json.dumps(data)})")
		print(f"[diag] renderDiagram returned: {result}", file=sys.stderr)
		page.wait_for_function("window.__renderComplete === true", timeout=30000)
		err = page.evaluate("window.__renderError")
		if err:
			print(f"[diag] __renderError = {err}", file=sys.stderr)
		root_html = page.evaluate("document.getElementById('root').innerHTML")
		print(f"[diag] root.innerHTML length = {len(root_html)}", file=sys.stderr)
		svg = page.query_selector("#root svg")
		if svg is None:
			print("ERROR: no SVG after render", file=sys.stderr)
			sys.exit(1)
		svg.screenshot(path=str(dst))
		browser.close()

	server.shutdown()
	print(dst)


if __name__ == "__main__":
	main()
