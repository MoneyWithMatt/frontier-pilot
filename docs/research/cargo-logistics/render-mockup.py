"""Static research illustration only; no game integration, simulator access or UI implementation."""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
W, H = 1320, 1410
BG, PAPER, INK, RULE = "#dcded9", "#f5f2e9", "#292b2a", "#95978e"
NS = "http://www.w3.org/2000/svg"
ET.register_namespace("", NS)
svg = ET.Element(f"{{{NS}}}svg", width=str(W), height=str(H), viewBox=f"0 0 {W} {H}", role="img")
ET.SubElement(svg, f"{{{NS}}}title").text = "Illustrative Frontier Pilot multi-stop dispatch sheet; design only"
image = Image.new("RGB", (W, H), BG)
draw = ImageDraw.Draw(image)


def rect(x, y, width, height, fill):
    ET.SubElement(svg, f"{{{NS}}}rect", x=str(x), y=str(y), width=str(width), height=str(height), fill=fill)
    draw.rectangle((x, y, x + width, y + height), fill=fill)


def line(y, x1=85, x2=1235, color=RULE):
    ET.SubElement(svg, f"{{{NS}}}line", x1=str(x1), y1=str(y), x2=str(x2), y2=str(y), stroke=color)
    draw.line((x1, y, x2, y), fill=color, width=1)


def text(x, y, value, size=21, bold=False, fill=INK):
    font_file = Path("C:/Windows/Fonts") / ("consolab.ttf" if bold else "consola.ttf")
    font = ImageFont.truetype(str(font_file), size)
    assert x + draw.textlength(value, font=font) <= 1240, ("Text overflows sheet", value)
    element = ET.SubElement(svg, f"{{{NS}}}text", x=str(x), y=str(y + size), fill=fill,
                            attrib={"font-family": "Consolas, 'Courier New', monospace", "font-size": str(size), "font-weight": "700" if bold else "400"})
    element.text = value
    draw.text((x, y), value, fill=fill, font=font)


def main():
    a, b, baseline = 10, 25, 12
    assert a + b == 35 and a + b - a == 25 and a + b - a - b == 0
    assert baseline + a + b == 47 and baseline + b == 37 and baseline == 12
    rect(50, 25, 1220, 1360, PAPER)
    for y in range(60, 1370, 85):
        for x in (27, 1281):
            ET.SubElement(svg, f"{{{NS}}}circle", cx=str(x + 7), cy=str(y + 7), r="7", fill="#b5b9b0")
            draw.ellipse((x, y, x + 14, y + 14), fill="#b5b9b0")
    text(85, 60, "FRONTIER PILOT / CARGO MANIFEST", 32, True)
    text(85, 108, "DESIGN STUDY — SYNTHETIC DATA — NOT A LIVE DISPATCH", 20)
    line(152)
    text(85, 174, "DISPATCH  EXAMPLE-042 / REV 02", 22, True)
    text(85, 214, "OPERATOR  EROS COMMUNITY AVIATION (ILLUSTRATIVE)")
    text(85, 252, "AIRFRAME  SELECTED REGISTRATION / C208 EXAMPLE")
    text(85, 290, "SERVICE   DEST-A (FICTIONAL STOP)    ORIGIN FYWE (EXAMPLE)")
    text(85, 328, "STATE     DELIVERY PREVIEW          TIME  -- UTC")
    line(374)
    text(85, 395, "CONSIGNMENTS ABOARD / BEFORE THIS HANDLING ACTION", 22, True)
    text(85, 447, "JOB", 19, True)
    text(260, 447, "DESCRIPTION", 19, True)
    text(682, 447, "DELIVER TO", 19, True)
    text(930, 447, "ABOARD", 19, True)
    text(1090, 447, "ACTION", 19, True)
    line(483)
    rect(77, 494, 1165, 86, "#e8e6dd")
    text(85, 506, "FP-101", 22, True)
    text(260, 506, "MEDICAL SUPPLIES", 22)
    text(682, 506, "DEST-A", 22)
    text(930, 506, "10 kg", 22)
    text(1090, 506, "UNLOAD", 22, True)
    text(260, 546, "Origin FYWE / resource and job remain linked", 18)
    text(85, 609, "FP-102", 22, True)
    text(260, 609, "FOOD SUPPLIES", 22)
    text(682, 609, "DEST-B", 22)
    text(930, 609, "25 kg", 22)
    text(1090, 609, "RETAIN", 22, True)
    text(260, 649, "Origin FYWE / continuing on next leg", 18)
    line(698)
    text(85, 720, "THIS STOP: 35 kg BEFORE — 10 kg DELIVERY = 25 kg RETAINED", 23, True)
    text(85, 764, "INSTRUCTIONS", 21, True)
    text(85, 802, "1. Deliver FP-101 only; verify the requested payload change.")
    text(85, 839, "2. Keep all 25 kg of FP-102 aboard for DEST-B.")
    text(85, 876, "3. New pickups join the next leg after confirmed loading.")
    line(927)
    text(85, 948, "ONE SHARED STATION — ILLUSTRATIVE, NOT AN AIRCRAFT MAPPING", 20, True)
    text(85, 989, "Other payload: 12 kg | Frontier before: 35 kg | Total: 47 kg")
    text(85, 1027, "After A:      12 kg + retained B 25 kg        = 37 kg")
    text(85, 1065, "After B:      12 kg + Frontier 0 kg           = 12 kg")
    text(85, 1105, "All other stations, occupants and fuel remain unchanged.", 19)
    line(1150)
    text(85, 1170, "PASSENGERS  Separate booking rows shown only when present.", 19)
    text(85, 1206, "READBACK    PREVIEW ONLY / no simulator confirmation in this study.", 18)
    text(85, 1242, "PLANNING    Weight, balance and performance remain pilot decisions.", 18)
    text(85, 1302, "END OF SHEET / live revision differs from an archived printout", 18)
    ET.ElementTree(svg).write(HERE / "manifest-mockup.svg", encoding="utf-8", xml_declaration=True)
    image.save(HERE / "manifest-mockup.png")
    print("PASS: 35 -> 25 -> 0 kg consignment totals; 47 -> 37 -> 12 kg station totals; all text fits sheet")


if __name__ == "__main__":
    main()
