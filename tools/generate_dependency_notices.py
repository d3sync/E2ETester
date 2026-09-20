"""Generate notices from restored NuGet metadata. Run after dotnet restore."""
import json
import pathlib
import shutil
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
packages = {}
for assets_path in (root / "src").glob("*/obj/project.assets.json"):
    data = json.loads(assets_path.read_text(encoding="utf-8"))
    for identity, lib in data["libraries"].items():
        if lib["type"] != "package":
            continue
        for folder in data["packageFolders"]:
            directory = pathlib.Path(folder) / lib["path"]
            nuspecs = list(directory.glob("*.nuspec"))
            if nuspecs:
                packages[identity] = (directory, nuspecs[0])
                break
notices = root / "docs/third-party"
notices.mkdir(exist_ok=True)
lines = ["# Third-party notices", "", "Generated from restored NuGet metadata by `tools/generate_dependency_notices.py`. Package versions are pinned in lockfiles. Dependencies retain their own licenses; the project's MIT license does not replace them. Bundled license/notice files supplied by packages are copied into `docs/third-party`.", "", "The self-contained distribution also includes Microsoft's .NET runtime. Edge WebView2 Runtime is installed separately under Microsoft's terms. Generated artwork provenance is in `assets/branding`.", "", "| Package / version | Authors | License |", "| --- | --- | --- |"]
for identity, (directory, nuspec) in sorted(packages.items(), key=lambda pair: pair[0].lower()):
    xml = ET.parse(nuspec).getroot()
    metadata = next(node for node in xml if node.tag.endswith("metadata"))
    fields = {node.tag.rsplit("}", 1)[-1]: node for node in metadata}
    def value(key):
        return (fields[key].text or "").strip() if key in fields else ""
    name, version = identity.split("/", 1)
    license_text = value("license") or value("licenseUrl") or "See package metadata"
    authors = value("authors").replace("|", "/")
    lines.append(f"| [{name} {version}](https://www.nuget.org/packages/{name}/{version}) | {authors} | {license_text.replace('|', '/')} |")
    candidates = [p for p in directory.iterdir() if p.is_file() and any(term in p.name.lower() for term in ("license", "notice", "third-party"))]
    if "license" in fields and fields["license"].get("type") == "file":
        candidates.append(directory / value("license"))
    for file in candidates:
        if file.is_file() and file.resolve().is_relative_to(directory.resolve()):
            target = notices / f"{name}-{version}-{file.name}"
            shutil.copyfile(file, target)
    copyright_text = value("copyright")
    if copyright_text:
        (notices / f"{name}-{version}-copyright.txt").write_text(copyright_text + "\n", encoding="utf-8")
(root / "THIRD_PARTY_NOTICES.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"Recorded {len(packages)} restored package licenses/notices.")
