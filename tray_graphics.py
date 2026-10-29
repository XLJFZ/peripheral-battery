from PIL import Image, ImageDraw
ICON_SIZE = 64
LOW_COLOR_LIMIT = 15
MID_COLOR_LIMIT = 30
def _fg_color():
    """跟随任务栏主题：深色任务栏用白线，浅色用深灰线，跟系统图标一致"""
    try:
        import winreg
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
        )
        light, _ = winreg.QueryValueEx(key, "SystemUsesLightTheme")
        winreg.CloseKey(key)
        return (32, 32, 32, 255) if light else (255, 255, 255, 255)
    except Exception:
        return (255, 255, 255, 255)


def _arc_color(v, fg):
    if v is None:
        return fg
    if v <= LOW_COLOR_LIMIT:
        return (255, 90, 90, 255)
    if v <= MID_COLOR_LIMIT:
        return (255, 196, 66, 255)
    return fg


def make_icon(mouse, keyboard):
    """两个同心细线圆环：外圈鼠标，内圈键盘。平时纯白/纯黑，低电量才变色。"""
    s = ICON_SIZE
    ss = 4  # 超采样，缩小后线条才不毛糙
    big = Image.new("RGBA", (s * ss, s * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    fg = _fg_color()
    track = (fg[0], fg[1], fg[2], 46)

    def ring(x0, y0, x1, y1, width, val):
        box = [x0 * ss, y0 * ss, x1 * ss, y1 * ss]
        w = max(1, int(round(width * ss)))
        d.ellipse(box, outline=track, width=w)
        if val is not None and val > 0:
            d.arc(
                box,
                start=-90,
                end=-90 + 360 * min(val, 100) / 100,
                fill=_arc_color(val, fg),
                width=w,
            )

    ring(3, 3, s - 4, s - 4, 3.8, mouse)          # 外圈：鼠标
    ring(19, 19, s - 20, s - 20, 3.0, keyboard)   # 内圈：键盘

    return big.resize((s, s), Image.LANCZOS)


def _fmt(v):
    return "--" if v is None else f"{v}%"



