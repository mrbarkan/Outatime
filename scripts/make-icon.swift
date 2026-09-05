#!/usr/bin/swift
// Renders the app icon: gradient tile + SF Symbol clock. Run from the repo root: swift scripts/make-icon.swift
// Output: Outatime/Assets.xcassets/AppIcon.appiconset (all macOS sizes via sips).
import AppKit

let out = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
    .appending(path: "Outatime/Assets.xcassets/AppIcon.appiconset")
let size = 1024.0
let image = NSImage(size: NSSize(width: size, height: size), flipped: false) { _ in
    // macOS icon grid: 824 pt tile centered on a 1024 canvas, corner radius ~ 22.5%.
    let tile = NSRect(x: 100, y: 100, width: 824, height: 824)
    let path = NSBezierPath(roundedRect: tile, xRadius: 185, yRadius: 185)
    NSGradient(colors: [NSColor(red: 0.98, green: 0.45, blue: 0.16, alpha: 1),
                        NSColor(red: 0.78, green: 0.16, blue: 0.42, alpha: 1),
                        NSColor(red: 0.29, green: 0.10, blue: 0.55, alpha: 1)])!
        .draw(in: path, angle: -60)
    let config = NSImage.SymbolConfiguration(pointSize: 520, weight: .medium)
    let symbol = NSImage(systemSymbolName: "clock", accessibilityDescription: nil)!
        .withSymbolConfiguration(config)!
    let tinted = NSImage(size: symbol.size, flipped: false) { rect in
        symbol.draw(in: rect)
        NSColor.white.set()
        rect.fill(using: .sourceAtop)
        return true
    }
    let s = tinted.size
    tinted.draw(in: NSRect(x: (size - s.width) / 2, y: (size - s.height) / 2, width: s.width, height: s.height))
    return true
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size), pixelsHigh: Int(size), bitsPerSample: 8,
                           samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                           bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
image.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
NSGraphicsContext.restoreGraphicsState()

try! FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
let master = out.appending(path: "icon_512x512@2x.png")
try! rep.representation(using: .png, properties: [:])!.write(to: master)

var images: [[String: String]] = []
for (pt, scale) in [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)] {
    let name = "icon_\(pt)x\(pt)@\(scale)x.png"
    images.append(["size": "\(pt)x\(pt)", "idiom": "mac", "scale": "\(scale)x", "filename": name])
    if pt * scale == 1024 { continue }
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/sips")
    p.arguments = ["-z", "\(pt * scale)", "\(pt * scale)", master.path, "--out", out.appending(path: name).path]
    p.standardOutput = FileHandle.nullDevice
    try! p.run(); p.waitUntilExit()
}
let contents = try! JSONSerialization.data(withJSONObject: ["images": images, "info": ["version": 1, "author": "xcode"]],
                                           options: [.prettyPrinted, .sortedKeys])
try! contents.write(to: out.appending(path: "Contents.json"))
print("Wrote \(images.count) icons to \(out.path)")
