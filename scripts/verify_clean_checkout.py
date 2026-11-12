"""Build/test a source-only copy selected by Git's ignore rules, optionally with a fresh venv."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import uuid
import venv
from pathlib import Path

from smoke import ROOT


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fresh-python", action="store_true")
    args = parser.parse_args()
    local = ROOT / ".local"
    local.mkdir(exist_ok=True)
    destination = local / ("clean-source-" + uuid.uuid4().hex[:10])
    source_paths = (
        subprocess.check_output(
            ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
            cwd=ROOT,
        )
        .decode()
        .split("\0")
    )
    directories = {
        "src",
        "tests",
        "scripts",
        "docs",
        "sample-data",
        "intelligence-service",
        ".config",
    }
    root_files = {
        "Argus.sln",
        "README.md",
        "AGENTS.md",
        ".gitignore",
        ".env.example",
        "global.json",
        "Directory.Build.props",
        "dotnet-tools.json",
        ".editorconfig",
        "NuGet.config",
    }
    for name in source_paths:
        if not name:
            continue
        path = Path(name)
        if (path.parts[0] not in directories and name not in root_files) or not (
            ROOT / path
        ).is_file():
            continue
        target = destination / path
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / path, target)
    if not (destination / "src/Argus.Infrastructure/Data/ArgusDbContext.cs").exists():
        raise RuntimeError("Git ignore rules excluded required database source code.")
    log_path = local / "clean-checkout.log"
    with log_path.open("w", encoding="utf-8") as log:

        def run(command: list[str]) -> None:
            print("Checking: " + " ".join(command[:3]), flush=True)
            subprocess.run(
                command,
                cwd=destination,
                stdout=log,
                stderr=subprocess.STDOUT,
                check=True,
                timeout=600,
            )

        run(["dotnet", "tool", "restore"])
        run(["dotnet", "restore", "Argus.sln"])
        run(["dotnet", "build", "Argus.sln", "-c", "Release", "--no-restore"])
        run(
            [
                "dotnet",
                "test",
                "Argus.sln",
                "-c",
                "Release",
                "--no-build",
                "--no-restore",
            ]
        )
        if args.fresh_python:
            print(
                "Creating a fresh Python environment from requirements-lock.txt",
                flush=True,
            )
            environment = destination / "intelligence-service/.venv"
            venv.EnvBuilder(with_pip=True).create(environment)
            python = environment / (
                "Scripts/python.exe" if os.name == "nt" else "bin/python"
            )
            run(
                [
                    str(python),
                    "-m",
                    "pip",
                    "install",
                    "-r",
                    "intelligence-service/requirements-lock.txt",
                ]
            )
        else:
            python = (
                ROOT
                / "intelligence-service/.venv"
                / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
            )
        run([str(python), "-m", "pip", "check"])
        run(
            [
                str(python),
                "-m",
                "pytest",
                "intelligence-service/tests",
                "-q",
                "-c",
                "intelligence-service/pyproject.toml",
            ]
        )
    print(
        f"PASS: source-only checkout build/tests{' and fresh Python setup' if args.fresh_python else ''}. Copy: {destination}; log: {log_path}"
    )


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        print(
            "Clean-checkout validation failed; inspect .local/clean-checkout.log",
            file=sys.stderr,
        )
        raise SystemExit(error.returncode) from error
