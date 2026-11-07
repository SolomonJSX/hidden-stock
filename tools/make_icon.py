from PIL import Image, ImageDraw

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# background
d.rounded_rectangle([10, 10, S - 11, S - 11], radius=int(S * 0.23),
                    fill=(23, 26, 33, 255), outline=(62, 68, 88, 255), width=12)

cx = S // 2
violet = (124, 92, 255, 255)
dark = (23, 26, 33, 255)

# shackle
w = int(S * 0.095)
left = cx - int(S * 0.19)
right = cx + int(S * 0.19)
top = int(S * 0.17)
bottom = int(S * 0.50)
d.arc([left, top, right, bottom], start=180, end=360, fill=violet, width=w)
d.rectangle([left, (top + bottom) // 2, left + w, int(S * 0.47)], fill=violet)
d.rectangle([right - w, (top + bottom) // 2, right, int(S * 0.47)], fill=violet)

# body
body_top = int(S * 0.43)
body_bottom = int(S * 0.83)
half = int(S * 0.27)
d.rounded_rectangle([cx - half, body_top, cx + half, body_bottom],
                    radius=int(S * 0.07), fill=violet)

# keyhole
d.ellipse([cx - int(S * 0.045), int(S * 0.53), cx + int(S * 0.045), int(S * 0.62)], fill=dark)
d.polygon([(cx - int(S * 0.022), int(S * 0.60)),
           (cx + int(S * 0.022), int(S * 0.60)),
           (cx + int(S * 0.032), int(S * 0.71)),
           (cx - int(S * 0.032), int(S * 0.71))], fill=dark)

img.save("icon.png")
img.save("icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
