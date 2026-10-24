"""Generate synthetic interoperability fixtures (reportlab and Pillow required)."""
import io
import json
from pathlib import Path
import zipfile

from PIL import Image, ImageDraw
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parent


def make_samples():
    form = ROOT / "template.pdf"
    c = canvas.Canvas(str(form), pagesize=(595, 842), invariant=1)
    c.setTitle("Synthetic PDF Template Helper example")
    c.setFont("Helvetica-Bold", 20)
    c.drawString(40, 790, "PDF Template Helper")
    c.setFont("Helvetica", 10)
    c.drawString(40, 770, "Synthetic form - no real customer information")
    for name, label, y in [("customer", "Customer", 700), ("date", "Date", 645),
                            ("reading", "Reading", 590)]:
        c.drawString(40, y + 28, label)
        c.acroForm.textfield(name=name, x=40, y=y, width=370, height=22,
                            borderWidth=1, fontSize=10, forceBorder=True)
    c.acroForm.checkbox(name="accepted", x=40, y=540, size=15,
                        buttonStyle="check", forceBorder=True)
    c.drawString(65, 544, "Accepted")
    c.drawString(40, 495, "Additional text:")
    c.rect(40, 360, 370, 100, stroke=1)
    c.drawString(40, 345, "Synthetic image area")
    c.drawString(40, 60, "Public example fixture; generated from make_samples.py")
    c.showPage()
    c.save()

    image = Image.new("RGB", (240, 100), "#eef5fa")
    draw = ImageDraw.Draw(image)
    draw.rectangle((5, 5, 234, 94), outline="#1d5375", width=4)
    draw.text((28, 40), "EXAMPLE ASSET", fill="#1d5375")
    image_bytes = io.BytesIO()
    image.save(image_bytes, format="PNG")
    request = {
        "fields": {"customer": "Čebelica Šimec - synthetic example", "date": "2026-10-24", "reading": "12.5"},
        "checkboxes": {"accepted": True},
        "stamps": [{"page": 1, "x": 40, "y": 475, "text": "Šumniki: č ć đ š ž Č Ć Đ Š Ž", "fontSize": 11}],
        "images": [{"page": 1, "x": 50, "y": 370, "width": 200, "height": 80,
                    "path": "assets/example.png", "fit": "Contain"}],
        "fontPath": "assets/NotoSans-Regular.ttf", "strict": True, "flatten": True,
    }
    font = ROOT.parent / "fonts" / "NotoSans-Regular.ttf"
    license_file = ROOT.parent / "fonts" / "OFL.txt"
    if not font.is_file() or not license_file.is_file():
        raise SystemExit("Put unmodified NotoSans-Regular.ttf and its OFL license in fonts first")

    (ROOT / "request.json").write_text(json.dumps(request, ensure_ascii=False, indent=2), encoding="utf-8")
    for filename, request_key, value in [
        ("sample.pdfjob", "request", request),
        ("sample-editable.pdfjob", "request", {**request, "flatten": False}),
        ("sample-batch.pdfjob", "requests", [request, {**request,
            "fields": {**request["fields"], "customer": "Second synthetic customer"},
            "checkboxes": {"accepted": False}}]),
    ]:
        job = {"protocolVersion": 1, "template": "template.pdf", request_key: value}
        with zipfile.ZipFile(ROOT / filename, "w", zipfile.ZIP_DEFLATED) as package:
            package.writestr("job.json", json.dumps(job, ensure_ascii=False, indent=2))
            package.write(form, "template.pdf")
            package.write(font, "assets/NotoSans-Regular.ttf")
            package.write(license_file, "assets/OFL.txt")
            package.writestr("assets/example.png", image_bytes.getvalue())
    print("Generated template and three synthetic jobs")


if __name__ == "__main__":
    make_samples()
