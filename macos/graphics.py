from PIL import Image, ImageDraw


def render(devices, colors, path):
    image = Image.new("RGBA", (144, 144))
    draw = ImageDraw.Draw(image)
    if not devices:
        draw.ellipse((14, 14, 130, 130), outline="#8B8B8B", width=10)
        draw.line((45, 72, 99, 72), fill="#8B8B8B", width=10)
    for index, device in enumerate(devices[:3]):
        inset = 8 + index * 22
        box = (inset, inset, 144 - inset, 144 - inset)
        draw.ellipse(box, outline=(128, 128, 128, 65), width=13)
        value = device.level
        color = colors[0 if value > 30 else 1 if value >= 10 else 2]
        if value:
            draw.arc(box, -90, -90 + 360 * value / 100, fill=color, width=13)
        else:
            draw.ellipse((66, inset - 2, 78, inset + 10), fill=color)
    image.resize((72, 72), Image.Resampling.LANCZOS).save(path, format="PNG")
