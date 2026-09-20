# E2E Tester identity

**Name:** E2E Tester  
**Descriptor:** Guided API testing  
**Mark:** An off-white timeline with green branches. Its three white nodes contain two green check marks and one red X, representing test outcomes and variations.

## Deliverables

- `e2e-tester-icon-master.png`: generated source artwork with transparent outer margins.
- `e2e-tester-icon-{16,24,32,48,64,128,256,512}.png`: application and documentation exports.
- `e2e-tester.ico`: Windows icon with seven resolutions, embedded in both desktop and MCP executables.
- `e2e-tester-wordmark-light.svg`: dark text for light surfaces.
- `e2e-tester-wordmark-dark.svg`: pale text for dark surfaces.
- `brand-guide.html`: visual guide and asset preview; open locally in a browser.
- `generation-prompt.md`: exact generation prompt and provenance.

Wordmark SVGs contain an embedded raster mark and editable text. Their text uses Segoe UI with Arial/sans-serif fallbacks. Font files are not distributed. Small icon exports are downsampled artwork, not individually hand-drawn pixel variants.

## Palette

| Role | Color |
| --- | --- |
| Brand green / dark-surface accent | `#3FB950` |
| Action green / light-surface button | `#1F883D` |
| Charcoal tile | `#161B22` |
| Dark canvas | `#0D1117` |
| Light canvas | `#F6F8FA` |
| Light-theme text | `#1F2328` |
| Dark-theme text | `#F0F6FC` |

Use Segoe UI for product text and headings, and Consolas for request/response evidence. Keep interface messages plain and specific: “Send request”, “Review result”, “Add variation”. The name is always “E2E Tester”.

## Usage

Use the icon alone in the taskbar, window title and compact header. Use the wordmark where there is enough space. Keep clear space of at least one quarter of the icon width around standalone presentations. Do not stretch, rotate or recolor the mark, add effects, or put it inside a second competing badge. Use the supplied dark tile on both light and dark backgrounds. The timeline is branding; PASS/FAIL/INCONCLUSIVE remain explicitly labeled in the app.

Rebuild the PNG/ICO/wordmark exports with `tools/Build-BrandAssets.ps1`. WPF assets are embedded resources, and publishing remains single-file per host. The source artwork was created with the built-in image-generation tool; the current prompt is in `revision-v4-prompt.md`.

The v2 master separates the check from the route for better recognition. Original artwork is retained as e2e-tester-icon-v1.png, and the edit prompt is in revision-v2-prompt.md.

The v3 branching timeline introduced the current structure. Earlier masters are retained as `e2e-tester-icon-v1.png` and `e2e-tester-icon-v2.png`.

The v4 master adds two green checks and a red X inside the white nodes. The v3 master is retained as `e2e-tester-icon-v3.png`. Actual test outcomes remain explicitly labeled in the UI.
