# LunaArc logo assets

- `app_logo.png`: the original 2048 x 2048 PNG supplied by the project owner as `anhnen.png`, copied without alteration. The current artwork features a tilted infinity loop with a moon and sun.
- `app_logo_256.png`: a 256 x 256 resized copy for the embedded About image, sparse Windows menu package and published `LunaArc.png` Linux launcher image.
- `app_logo.ico`: PNG-backed Windows icon with 16, 24, 32, 48, 64, 128 and 256 pixel sizes, all resized from the original image.
- `app_logo.svg`: the previous logo, retained as a legacy asset; current application and packaging paths use the supplied PNG/ICO.

The derived files preserve the original composition and colors. They were produced with System.Drawing using high-quality bicubic resizing; no background removal or redraw was applied. Builds copy these committed assets and do not generate a substitute logo.

Studio embeds the ICO and 256-pixel PNG as Avalonia resources. Both GUI and CLI Windows executables embed the ICO. Inno Setup uses the same ICO, and the native Explorer menu package ships it as `LunaArc.Menu.ico`. Both publish scripts include the 256-pixel PNG as `LunaArc.png`; Linux desktop registration uses its absolute path when available.
