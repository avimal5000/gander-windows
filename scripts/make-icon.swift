// Renders Gander's Windows icon (the goose mark; Gander.App's GanderMarkGeometry holds the same outline) as a multi-size .ico
// with PNG frames, plus a preview PNG. Runs on a Mac:  swift scripts/make-icon.swift src/Gander.App/Assets/Gander.ico src/Gander.App/Assets/Gander.preview.png
import AppKit
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

let forest = CGColor(srgbRed: 0x2F / 255, green: 0x5D / 255, blue: 0x3A / 255, alpha: 1)
let forestTop = CGColor(srgbRed: 0x37 / 255, green: 0x6B / 255, blue: 0x44 / 255, alpha: 1)
let sage = CGColor(srgbRed: 0x9C / 255, green: 0xAF / 255, blue: 0x88 / 255, alpha: 1)
let ivory = CGColor(srgbRed: 0xF6 / 255, green: 0xF3 / 255, blue: 0xEA / 255, alpha: 1)

func render(px: Int) -> CGImage {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let ctx = CGContext(
        data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0, space: space,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    let side = CGFloat(px)
    // A rounded tile that fills the canvas, as Windows icons do.
    let tile = CGRect(x: 0, y: 0, width: side, height: side).insetBy(dx: side * 0.02, dy: side * 0.02)
    let shape = CGPath(roundedRect: tile, cornerWidth: tile.width * 0.225, cornerHeight: tile.width * 0.225, transform: nil)
    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()
    let gradient = CGGradient(colorsSpace: space, colors: [forestTop, forest] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: side), end: CGPoint(x: 0, y: 0), options: [])
    ctx.restoreGState()

    // The goose, in its own coordinates (y down), fitted into the tile: 68% of its height (74% at tiny sizes), centred.
    let bounds = CGRect(x: 14, y: 10.9, width: 68.3, height: 75.8)
    let scale = tile.height * (px <= 32 ? 0.76 : 0.68) / bounds.height
    func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
        CGPoint(x: tile.midX + (x - bounds.midX) * scale, y: tile.midY - (y - bounds.midY) * scale)
    }
    let body = CGMutablePath()
    body.move(to: p(14, 70))
    body.addCurve(to: p(42, 64), control1: p(22, 72), control2: p(32, 66))
    body.addCurve(to: p(49, 50), control1: p(48, 63), control2: p(50, 58))
    body.addCurve(to: p(47, 26), control1: p(48, 40), control2: p(46, 34))
    body.addCurve(to: p(57, 11), control1: p(47.5, 18), control2: p(50, 12))
    body.addCurve(to: p(66, 15.5), control1: p(62, 10.5), control2: p(65, 13))
    body.addLine(to: p(76.5, 18))
    body.addLine(to: p(67, 22.5))
    body.addCurve(to: p(60.5, 28), control1: p(64, 23.5), control2: p(61.5, 24.5))
    body.addCurve(to: p(62, 46), control1: p(59.5, 34), control2: p(60.5, 40))
    body.addCurve(to: p(78, 60), control1: p(64, 52), control2: p(72, 54))
    body.addCurve(to: p(70, 84), control1: p(86, 68), control2: p(82, 80))
    body.addCurve(to: p(14, 70), control1: p(54, 90), control2: p(28, 86))
    body.closeSubpath()
    let eyeRadius = (px <= 32 ? 2.6 : 1.6) * scale
    let eye = p(60.8, 17.1)
    body.addEllipse(in: CGRect(x: eye.x - eyeRadius, y: eye.y - eyeRadius, width: eyeRadius * 2, height: eyeRadius * 2))
    ctx.addPath(body)
    ctx.setFillColor(ivory)
    ctx.fillPath(using: .evenOdd)

    if px > 32 {
        let wing = CGMutablePath()
        wing.move(to: p(32, 73))
        wing.addCurve(to: p(69, 71), control1: p(42, 65), control2: p(58, 65))
        wing.addCurve(to: p(32, 73), control1: p(60, 80), control2: p(42, 80))
        wing.closeSubpath()
        ctx.addPath(wing)
        ctx.setFillColor(sage)
        ctx.fillPath()
    }
    return ctx.makeImage()!
}

func png(_ image: CGImage) -> Data {
    let data = NSMutableData()
    let destination = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else { fatalError("Could not encode a PNG") }
    return data as Data
}

func le16(_ v: Int) -> [UInt8] { [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF)] }
func le32(_ v: Int) -> [UInt8] { [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF), UInt8((v >> 16) & 0xFF), UInt8((v >> 24) & 0xFF)] }

let sizes = [16, 24, 32, 48, 64, 128, 256]
let frames = sizes.map { png(render(px: $0)) }
var ico = Data()
ico.append(contentsOf: le16(0) + le16(1) + le16(sizes.count))
var offset = 6 + 16 * sizes.count
for (size, frame) in zip(sizes, frames) {
    ico.append(contentsOf: [UInt8(size == 256 ? 0 : size), UInt8(size == 256 ? 0 : size), 0, 0])
    ico.append(contentsOf: le16(1) + le16(32) + le32(frame.count) + le32(offset))
    offset += frame.count
}
for frame in frames { ico.append(frame) }
try ico.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("wrote \(CommandLine.arguments[1]) with sizes \(sizes)")
if CommandLine.arguments.count > 2 {
    try png(render(px: 512)).write(to: URL(fileURLWithPath: CommandLine.arguments[2]))
}
