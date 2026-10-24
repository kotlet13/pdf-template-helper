"""Independent protocol-1 client. Uses only Python's standard library."""
import argparse
import json
import pathlib
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--helper", required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    helper = str(pathlib.Path(args.helper).resolve())
    capabilities = subprocess.run([helper, "capabilities"], check=True,
                                  capture_output=True, text=True, timeout=15)
    data = json.loads(capabilities.stdout)
    if data.get("protocolVersion") != 1 or "fill" not in data.get("operations", []):
        raise SystemExit("Helper does not support protocol 1 fill")
    output = pathlib.Path(args.out).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run([helper, "fill", "--job", str(pathlib.Path(args.job).resolve()),
                             "--out", str(output)],
                            capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise SystemExit(result.stderr or result.stdout)
    response = json.loads(result.stdout)
    if response.get("status") != "ok" or not output.is_file():
        raise SystemExit("Helper returned no PDF")
    with output.open("rb") as pdf:
        if pdf.read(5) != b"%PDF-":
            raise SystemExit("Output is not a PDF")
    print(json.dumps(response, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
