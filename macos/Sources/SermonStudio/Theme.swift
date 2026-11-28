import AppKit
import SwiftUI

/// Sermon Studio's navy-and-gold identity, shared with the Windows app (navy 20/36/55, gold 190/151/69).
enum Theme {
    static let gold = Color(red: 190 / 255, green: 151 / 255, blue: 69 / 255)
    static let navy = Color(red: 20 / 255, green: 36 / 255, blue: 55 / 255)
    /// Navy in light mode; a lighter, readable navy-blue in dark mode.
    static let accent = Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .vibrantDark]) != nil
            ? NSColor(red: 0.55, green: 0.68, blue: 0.86, alpha: 1)
            : NSColor(red: 20 / 255, green: 36 / 255, blue: 55 / 255, alpha: 1)
    })
    static let panel = Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .vibrantDark]) != nil
            ? NSColor(red: 25 / 255, green: 43 / 255, blue: 65 / 255, alpha: 0.55)
            : NSColor(red: 20 / 255, green: 36 / 255, blue: 55 / 255, alpha: 0.045)
    })

    static func serif(_ style: Font.TextStyle) -> Font { .system(style, design: .serif) }
}

struct Card<Content: View>: View {
    var title: String?
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let title { Text(title).font(Theme.serif(.headline)).foregroundStyle(Theme.accent) }
            content
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.panel, in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Theme.gold.opacity(0.25)))
    }
}

/// Primary call-to-action style in brand colours. In Increase Contrast mode it falls back to the system style.
struct ProminentStyle: ButtonStyle {
    @Environment(\.colorSchemeContrast) private var contrast
    @Environment(\.isEnabled) private var isEnabled
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .padding(.horizontal, 14).padding(.vertical, 7)
            .foregroundStyle(contrast == .increased ? Color.primary : Color.white)
            .background(contrast == .increased ? Color.clear : Theme.navy.opacity(configuration.isPressed ? 0.8 : 1), in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(contrast == .increased ? Color.primary : Theme.gold, lineWidth: contrast == .increased ? 2 : 1))
            .opacity(isEnabled ? 1 : 0.45)
    }
}
