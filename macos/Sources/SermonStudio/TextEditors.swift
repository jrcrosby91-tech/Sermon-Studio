import AppKit
import SwiftUI
import SermonCore

enum StudioFonts {
    static func serif(_ size: CGFloat, bold: Bool = false, italic: Bool = false) -> NSFont {
        var font = NSFont(name: "Georgia", size: size) ?? NSFont.systemFont(ofSize: size)
        let manager = NSFontManager.shared
        if bold { font = manager.convert(font, toHaveTrait: .boldFontMask) }
        if italic { font = manager.convert(font, toHaveTrait: .italicFontMask) }
        return font
    }
}

/// Requests that an editor select and scroll to a range (find next/previous, timestamp and source lookup).
struct TextFocusRequest: Equatable {
    var range: NSRange
    var token = UUID()
}

private func makeScrollableTextView() -> (NSScrollView, NSTextView) {
    let scroll = NSTextView.scrollableTextView()
    let textView = scroll.documentView as! NSTextView
    scroll.hasVerticalScroller = true
    scroll.borderType = .noBorder
    textView.isRichText = false
    textView.allowsUndo = true
    textView.isContinuousSpellCheckingEnabled = true
    textView.isAutomaticQuoteSubstitutionEnabled = false
    textView.isAutomaticDashSubstitutionEnabled = false
    textView.isAutomaticTextReplacementEnabled = false
    textView.textContainerInset = NSSize(width: 10, height: 12)
    textView.usesFindBar = true
    textView.isIncrementalSearchingEnabled = true
    textView.drawsBackground = true
    textView.backgroundColor = .textBackgroundColor
    return (scroll, textView)
}

/// Plain-text editor (transcripts and Markdown source) backed by NSTextView: native undo, spell checking,
/// find highlighting and precise selection.
struct PlainTextEditor: NSViewRepresentable {
    @Binding var text: String
    var fontSize: Double
    var serif = false
    var highlights: [NSRange] = []
    var focus: TextFocusRequest?
    var accessibilityLabel: String
    var onSelectionChange: ((NSRange) -> Void)?

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        let (scroll, textView) = makeScrollableTextView()
        textView.delegate = context.coordinator
        textView.string = text
        textView.setAccessibilityLabel(accessibilityLabel)
        context.coordinator.textView = textView
        apply(textView)
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        context.coordinator.parent = self
        guard let textView = context.coordinator.textView else { return }
        if textView.string != text {
            let selection = textView.selectedRange()
            textView.string = text
            let length = (text as NSString).length
            textView.setSelectedRange(NSRange(location: min(selection.location, length), length: 0))
        }
        apply(textView)
        if let focus, focus != context.coordinator.lastFocus {
            context.coordinator.lastFocus = focus
            let length = (textView.string as NSString).length
            if focus.range.location != NSNotFound, focus.range.location + focus.range.length <= length {
                textView.setSelectedRange(focus.range)
                textView.scrollRangeToVisible(focus.range)
                textView.showFindIndicator(for: focus.range)
            }
        }
    }

    private func apply(_ textView: NSTextView) {
        let font = serif ? StudioFonts.serif(fontSize) : NSFont.systemFont(ofSize: fontSize)
        if textView.font != font { textView.font = font }
        guard let layout = textView.layoutManager else { return }
        let full = NSRange(location: 0, length: (textView.string as NSString).length)
        layout.removeTemporaryAttribute(.backgroundColor, forCharacterRange: full)
        for range in highlights where range.location + range.length <= full.length {
            layout.addTemporaryAttribute(.backgroundColor, value: NSColor.systemYellow.withAlphaComponent(0.35), forCharacterRange: range)
        }
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: PlainTextEditor
        weak var textView: NSTextView?
        var lastFocus: TextFocusRequest?
        init(_ parent: PlainTextEditor) { self.parent = parent }

        func textDidChange(_ notification: Notification) {
            guard let textView else { return }
            if parent.text != textView.string { parent.text = textView.string }
        }

        func textViewDidChangeSelection(_ notification: Notification) {
            guard let textView, let handler = parent.onSelectionChange else { return }
            let range = textView.selectedRange()
            DispatchQueue.main.async { handler(range) }
        }
    }
}

/// Commands for the visual outline editor; the toolbar talks to the NSTextView through this object.
@MainActor final class RichEditorController: ObservableObject {
    weak var textView: NSTextView?
    var baseSize: Double = 18
    /// Writes pending visual edits to the Markdown binding immediately.
    var flushHandler: (() -> Void)?

    func flush() { flushHandler?() }

    private func paragraphRange() -> NSRange? {
        guard let textView else { return nil }
        return (textView.string as NSString).paragraphRange(for: textView.selectedRange())
    }

    private func edit(_ range: NSRange, _ body: (NSTextStorage) -> Void) {
        guard let textView, let storage = textView.textStorage, textView.shouldChangeText(in: range, replacementString: nil) else { return }
        storage.beginEditing(); body(storage); storage.endEditing()
        textView.didChangeText()
    }

    func setBlock(size: Double, bold: Bool) {
        guard let range = paragraphRange() else { return }
        edit(range) { storage in
            storage.enumerateAttribute(.font, in: range) { value, sub, _ in
                let italic = (value as? NSFont).map { NSFontManager.shared.traits(of: $0).contains(.italicFontMask) } ?? false
                storage.addAttribute(.font, value: StudioFonts.serif(size, bold: bold, italic: italic), range: sub)
            }
        }
    }

    func heading() { setBlock(size: OutlineMarkdown.headingSize(baseSize), bold: true) }
    func subheading() { setBlock(size: OutlineMarkdown.subheadingSize(baseSize), bold: true) }
    func normal() { setBlock(size: baseSize, bold: false) }

    func toggle(_ trait: NSFontTraitMask) {
        guard let textView, let storage = textView.textStorage else { return }
        let range = textView.selectedRange()
        guard range.length > 0 else {
            var attributes = textView.typingAttributes
            let font = attributes[.font] as? NSFont ?? StudioFonts.serif(baseSize)
            let has = NSFontManager.shared.traits(of: font).contains(trait)
            attributes[.font] = has ? NSFontManager.shared.convert(font, toNotHaveTrait: trait) : NSFontManager.shared.convert(font, toHaveTrait: trait)
            textView.typingAttributes = attributes
            return
        }
        var allHave = true
        storage.enumerateAttribute(.font, in: range) { value, _, stop in
            if let font = value as? NSFont, !NSFontManager.shared.traits(of: font).contains(trait) { allHave = false; stop.pointee = true }
        }
        edit(range) { storage in
            storage.enumerateAttribute(.font, in: range) { value, sub, _ in
                let font = value as? NSFont ?? StudioFonts.serif(baseSize)
                let converted = allHave ? NSFontManager.shared.convert(font, toNotHaveTrait: trait) : NSFontManager.shared.convert(font, toHaveTrait: trait)
                storage.addAttribute(.font, value: converted, range: sub)
            }
        }
    }

    /// Adds or removes a line prefix (bullet, quotation, number) on every selected paragraph.
    func prefix(_ marker: String, numbered: Bool = false) {
        guard let textView, let range = paragraphRange() else { return }
        let text = (textView.string as NSString).substring(with: range)
        var lines = text.components(separatedBy: "\n")
        let trailingNewline = text.hasSuffix("\n")
        if trailingNewline { lines.removeLast() }
        let numberPattern = "^[0-9]{1,3}\\. "
        let allHave = lines.allSatisfy { numbered ? $0.range(of: numberPattern, options: .regularExpression) != nil : $0.hasPrefix(marker) }
        var counter = 0
        let updated = lines.map { line -> String in
            if allHave {
                if numbered, let r = line.range(of: numberPattern, options: .regularExpression) { return String(line[r.upperBound...]) }
                return line.hasPrefix(marker) ? String(line.dropFirst(marker.count)) : line
            }
            var body = line
            for other in [OutlineMarkdown.visualBullet, OutlineMarkdown.visualQuote] where body.hasPrefix(other) { body = String(body.dropFirst(other.count)) }
            if let r = body.range(of: numberPattern, options: .regularExpression) { body = String(body[r.upperBound...]) }
            counter += 1
            return (numbered ? "\(counter). " : marker) + body
        }.joined(separator: "\n") + (trailingNewline ? "\n" : "")
        guard textView.shouldChangeText(in: range, replacementString: updated) else { return }
        let storageLength = textView.textStorage?.length ?? 0
        let attributes = storageLength > 0 ? (textView.textStorage?.attributes(at: min(range.location, storageLength - 1), effectiveRange: nil) ?? textView.typingAttributes) : textView.typingAttributes
        textView.textStorage?.replaceCharacters(in: range, with: NSAttributedString(string: updated, attributes: attributes))
        textView.didChangeText()
        if marker == OutlineMarkdown.visualQuote && !allHave {
            let newRange = NSRange(location: range.location, length: (updated as NSString).length)
            edit(newRange) { storage in
                storage.enumerateAttribute(.font, in: newRange) { value, sub, _ in
                    let font = value as? NSFont ?? StudioFonts.serif(self.baseSize)
                    storage.addAttribute(.font, value: NSFontManager.shared.convert(font, toHaveTrait: .italicFontMask), range: sub)
                }
            }
        }
    }

    func undo() { textView?.undoManager?.undo() }
    func redo() { textView?.undoManager?.redo() }
}

/// Visual (rich-text) outline editor. Markdown is the stored format; the editor converts on load and, after a
/// short pause in typing, writes back only the lines the user actually changed.
struct RichOutlineEditor: NSViewRepresentable {
    @Binding var markdown: String
    var baseSize: Double
    @ObservedObject var controller: RichEditorController

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        let (scroll, textView) = makeScrollableTextView()
        textView.isRichText = true
        textView.importsGraphics = false
        textView.delegate = context.coordinator
        textView.setAccessibilityLabel("Outline editor, formatted")
        context.coordinator.textView = textView
        controller.textView = textView
        controller.baseSize = baseSize
        controller.flushHandler = { [weak coordinator = context.coordinator] in coordinator?.store() }
        context.coordinator.load(markdown)
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        let coordinator = context.coordinator
        coordinator.parent = self
        controller.textView = coordinator.textView
        if coordinator.loadedSize != baseSize {
            coordinator.store()
            controller.baseSize = baseSize
            coordinator.load(markdown)
        } else if markdown != coordinator.lastMarkdown && !coordinator.dirty {
            coordinator.load(markdown)
        }
    }

    static func dismantleNSView(_ scroll: NSScrollView, coordinator: Coordinator) {
        coordinator.store()
        coordinator.timer?.invalidate()
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: RichOutlineEditor
        weak var textView: NSTextView?
        var lastMarkdown = ""
        var loadedSize: Double = 0
        var dirty = false
        var syncing = false
        var timer: Timer?
        var resetTypingAfterNewline = false

        init(_ parent: RichOutlineEditor) { self.parent = parent }

        func load(_ markdown: String) {
            guard let textView, let storage = textView.textStorage else { return }
            syncing = true
            defer { syncing = false }
            let base = parent.baseSize
            let visual = OutlineMarkdown.toVisual(markdown, baseSize: base)
            let attributed = NSMutableAttributedString(string: visual.text, attributes: [.font: StudioFonts.serif(base), .foregroundColor: NSColor.textColor])
            for span in visual.spans {
                attributed.addAttribute(.font, value: StudioFonts.serif(span.size, bold: span.bold, italic: span.italic), range: NSRange(location: span.start, length: span.end - span.start))
            }
            let selection = textView.selectedRange()
            storage.setAttributedString(attributed)
            textView.typingAttributes = [.font: StudioFonts.serif(base), .foregroundColor: NSColor.textColor]
            textView.setSelectedRange(NSRange(location: min(selection.location, attributed.length), length: 0))
            textView.undoManager?.removeAllActions()
            lastMarkdown = markdown
            loadedSize = base
            dirty = false
        }

        /// Converts the rich text to Markdown and merges it with the stored outline.
        func store() {
            timer?.invalidate(); timer = nil
            guard dirty, !syncing, let textView, let storage = textView.textStorage else { return }
            let base = loadedSize > 0 ? loadedSize : parent.baseSize
            let plain = storage.string
            let length = storage.length
            func font(_ index: Int) -> NSFont? { index < length ? storage.attribute(.font, at: index, effectiveRange: nil) as? NSFont : nil }
            let updated = OutlineMarkdown.fromVisual(plain, baseSize: base, sizeAt: { Double(font($0)?.pointSize ?? CGFloat(base)) }, formatAt: { index in
                guard let f = font(index) else { return (false, false) }
                let traits = NSFontManager.shared.traits(of: f)
                return (traits.contains(.boldFontMask), traits.contains(.italicFontMask))
            })
            let merged = OutlineMarkdown.mergeVisualEdit(original: lastMarkdown, updated: updated, baseSize: base)
            dirty = false
            lastMarkdown = merged
            if OutlineMarkdown.normalizeNewlines(parent.markdown) != merged { parent.markdown = merged }
        }

        func textView(_ textView: NSTextView, shouldChangeTextIn range: NSRange, replacementString: String?) -> Bool {
            if replacementString == "\n" { resetTypingAfterNewline = true }
            return true
        }

        func textDidChange(_ notification: Notification) {
            guard !syncing, let textView else { return }
            if resetTypingAfterNewline {
                resetTypingAfterNewline = false
                // A new line after a heading or quotation starts as body text.
                textView.typingAttributes = [.font: StudioFonts.serif(parent.baseSize), .foregroundColor: NSColor.textColor]
            }
            dirty = true
            timer?.invalidate()
            timer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: false) { [weak self] _ in self?.store() }
        }
    }
}

/// Formatted reading view with Scripture links.
struct OutlineReadingView: View {
    var markdown: String
    var fontSize: Double
    var accent: Color

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 8) {
                let lines = OutlineMarkdown.parseDocument(markdown).filter { !$0.runs.isEmpty }
                if lines.isEmpty { Text("Write or generate an outline to see it formatted here.").foregroundStyle(.secondary) }
                ForEach(Array(lines.enumerated()), id: \.offset) { _, line in
                    Text(attributed(line))
                        .font(.custom("Georgia", size: size(line)))
                        .padding(.leading, [.quote, .bullet, .numbered].contains(line.block) ? 18 : 0)
                        .padding(.top, line.block == .heading ? 8 : 0)
                        .foregroundStyle(line.block == .heading || line.block == .quote ? accent : .primary)
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
            .padding(16)
        }
    }

    private func size(_ line: OutlineLine) -> Double {
        line.block == .heading ? (line.level <= 2 ? OutlineMarkdown.headingSize(fontSize) : OutlineMarkdown.subheadingSize(fontSize)) : fontSize
    }

    private func attributed(_ line: OutlineLine) -> AttributedString {
        var output = AttributedString(line.block == .bullet ? "• " : line.block == .numbered ? line.marker : "")
        for run in line.runs {
            for part in ScriptureReferences.linkSegments(run.text) {
                var piece = AttributedString(part.text)
                var intent = InlinePresentationIntent()
                if run.bold || line.block == .heading { intent.insert(.stronglyEmphasized) }
                if run.italic || line.block == .quote { intent.insert(.emphasized) }
                piece.inlinePresentationIntent = intent
                if let url = part.url { piece.link = url; piece.underlineStyle = .single }
                output += piece
            }
        }
        return output
    }
}
