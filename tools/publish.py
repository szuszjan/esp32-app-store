"""ESP32 App Store publishing.

  python tools/publish.py collect [ids]   copy the built binaries into dist/, write catalog.local.json
                                          (relative paths, for testing in the app) and catalog.json
  python tools/publish.py build   [ids]   compile with arduino-cli first, then collect
  python tools/publish.py release [ids]   GitHub: one release per app version (tag <id>-v<version>)
                                          with its binaries, then commit + push catalog.json

No ids = every app in apps.json whose "publish" is not false. Each app is written as four files at
the offsets arduino-cli's own upload uses: bootloader 0x0, partitions 0x8000, boot_app0 0xe000,
application 0x10000.
"""
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECTS = os.path.dirname(ROOT)
MANIFEST = json.load(open(os.path.join(ROOT, "apps.json"), encoding="utf-8"))
REPO = MANIFEST["repo"]
DIST = os.path.join(ROOT, "dist")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def pick(ids):
    apps = MANIFEST["apps"]
    if ids:
        out = [a for a in apps if a["id"] in ids]
        missing = set(ids) - {a["id"] for a in out}
        if missing:
            sys.exit("unknown app id(s): " + ", ".join(sorted(missing)))
        return out
    return [a for a in apps if a.get("publish", True)]


def version_of(app):
    src = open(os.path.join(PROJECTS, app["version_file"]), encoding="utf-8", errors="replace").read()
    m = re.search(app["version_regex"], src)
    if not m:
        sys.exit(f"{app['id']}: version not found in {app['version_file']}")
    return m.group(1)


def build(app):
    cmd = [MANIFEST["arduino_cli"], "compile", "--fqbn", app["fqbn"], "--build-path", app["build_path"]]
    for p in app.get("build_props", []):
        cmd += ["--build-property", p]
    cmd.append(os.path.join(PROJECTS, app["sketch"]))
    print(f"[{app['id']}] building ({app['fqbn']})")
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print(r.stdout[-3000:], r.stderr[-3000:])
        sys.exit(f"{app['id']}: build failed")


def collect(app):
    ver = version_of(app)
    bp, sk = app["build_path"], app["sketch"]
    srcs = {
        "bootloader.bin": (os.path.join(bp, f"{sk}.ino.bootloader.bin"), "0x0"),
        "partitions.bin": (os.path.join(bp, f"{sk}.ino.partitions.bin"), "0x8000"),
        "boot_app0.bin": (MANIFEST["boot_app0"], "0xe000"),
        "app.bin": (os.path.join(bp, f"{sk}.ino.bin"), "0x10000"),
    }
    for name, (path, _) in srcs.items():
        if not os.path.exists(path):
            sys.exit(f"{app['id']}: {path} missing - run 'build {app['id']}' first")
    app_bin = srcs["app.bin"][0]
    # the binary must be the version the source says; a stale build would be published under the wrong number
    if ver.encode() not in open(app_bin, "rb").read():
        print(f"  WARNING [{app['id']}]: '{ver}' is not inside {os.path.basename(app_bin)} - the build may be older than the source")
    src_file = os.path.join(PROJECTS, app["version_file"])
    if os.path.getmtime(src_file) > os.path.getmtime(app_bin) + 5:
        print(f"  WARNING [{app['id']}]: {app['version_file']} changed after the last build")
    out = os.path.join(DIST, app["id"], ver)
    os.makedirs(out, exist_ok=True)
    files = []
    for name, (path, offset) in srcs.items():
        dst = os.path.join(out, name)
        shutil.copyfile(path, dst)
        files.append({"name": name, "offset": offset, "size": os.path.getsize(dst), "sha256": sha256(dst),
                      "local": os.path.relpath(dst, ROOT).replace("\\", "/")})
    built = datetime.datetime.fromtimestamp(os.path.getmtime(app_bin)).strftime("%Y-%m-%d")
    print(f"[{app['id']}] v{ver}  {sum(f['size'] for f in files) / 1048576:.2f} MB  built {built}")
    return ver, files, built


def entry(app, ver, files, built, local):
    tag = f"{app['id']}-v{ver}"
    return {
        "id": app["id"], "name": app["name"], "version": ver, "device": app["device"], "chip": app["chip"],
        "category": app["category"], "description": app["description"], "notes": app.get("notes", ""),
        "flash_size": app.get("flash_size", ""), "reset": app.get("reset", "default"), "baud": app.get("baud", 921600),
        "released": built,
        "files": [{"name": f["name"], "offset": f["offset"], "size": f["size"], "sha256": f["sha256"],
                   "url": f["local"] if local else f"https://github.com/{REPO}/releases/download/{tag}/{f['name']}"}
                  for f in files],
    }


def write_catalogs(results):
    for local, fname in ((True, "catalog.local.json"), (False, "catalog.json")):
        path = os.path.join(ROOT, fname)
        doc = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {"version": 1, "apps": []}
        byid = {a["id"]: a for a in doc["apps"]}
        for app, ver, files, built in results:
            byid[app["id"]] = entry(app, ver, files, built, local)
        doc["apps"] = sorted(byid.values(), key=lambda a: a["name"].lower())
        doc["updated"] = datetime.date.today().isoformat()
        json.dump(doc, open(path, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
        print(f"wrote {fname} ({len(doc['apps'])} apps)")


def gh(*args, check=True):
    r = subprocess.run(["gh", *args], capture_output=True, text=True)
    if check and r.returncode != 0:
        sys.exit(f"gh {' '.join(args)} failed:\n{r.stderr}")
    return r


def release(results):
    for app, ver, files, built in results:
        tag = f"{app['id']}-v{ver}"
        paths = [os.path.join(ROOT, f["local"]) for f in files]
        if gh("release", "view", tag, "--repo", REPO, check=False).returncode == 0:
            print(f"[{app['id']}] release {tag} exists: replacing its files")
            gh("release", "upload", tag, *paths, "--repo", REPO, "--clobber")
        else:
            print(f"[{app['id']}] creating release {tag}")
            gh("release", "create", tag, *paths, "--repo", REPO, "--title", f"{app['name']} {ver}",
               "--notes", f"{app['name']} {ver} for {app['device']} ({app['chip']}). Install with the ESP32 App Store.")
    subprocess.run(["git", "-C", ROOT, "add", "catalog.json"], check=True)
    if subprocess.run(["git", "-C", ROOT, "diff", "--cached", "--quiet"]).returncode != 0:
        names = ", ".join(f"{a['id']} {v}" for a, v, _, _ in results)
        subprocess.run(["git", "-C", ROOT, "commit", "-m", f"catalog: {names}"], check=True)
    subprocess.run(["git", "-C", ROOT, "push"], check=True)


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ("collect", "build", "release"):
        sys.exit(__doc__)
    cmd, ids = sys.argv[1], sys.argv[2:]
    apps = pick(ids)
    if cmd == "build":
        for a in apps:
            build(a)
    results = [(a, *collect(a)) for a in apps]
    write_catalogs(results)
    if cmd == "release":
        release(results)


if __name__ == "__main__":
    main()
