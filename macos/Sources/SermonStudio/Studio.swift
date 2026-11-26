import AppKit
import SwiftUI
import Sparkle
import SermonCore

/// Sparkle updates (signed appcast). Present only when the build was given the trusted public key.
@MainActor final class StudioUpdates: ObservableObject {
    static let shared = StudioUpdates()
    let controller: SPUStandardUpdaterController?
    private init() {
        if Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") != nil {
            controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)
        } else { controller = nil }
    }
}

struct UpdateControls: View {
    @ObservedObject private var updates = StudioUpdates.shared
    @State private var automatic = false

    var body: some View {
        if let controller = updates.controller {
            HStack {
                Button("Check for Updates…") { controller.checkForUpdates(nil) }
                Toggle("Check automatically", isOn: Binding(get: { automatic }, set: { automatic = $0; controller.updater.automaticallyChecksForUpdates = $0 }))
            }
            .onAppear { automatic = controller.updater.automaticallyChecksForUpdates }
            Text("Updates are signed and installed only with your approval. Save your work first; your library and settings are kept.").font(.caption).foregroundStyle(.secondary)
        } else {
            Text("This build is not connected to the signed update feed.").foregroundStyle(.secondary)
        }
    }
}

final class StudioAppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        MainActor.assumeIsolated { AppModel.shared.shouldTerminate() }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

@main struct StudioApp: App {
    @NSApplicationDelegateAdaptor(StudioAppDelegate.self) private var delegate
    @StateObject private var model = AppModel.shared

    var body: some Scene {
        Window("Sermon Studio", id: "main") {
            StudioView(model: model).frame(minWidth: 900, minHeight: 600)
        }
        .defaultSize(width: 1240, height: 800)
        .commands {
            SidebarCommands()
            CommandGroup(replacing: .newItem) {
                Button("New Sermon") { model.newSermon() }.keyboardShortcut("n")
                Button("Open Recording…") { model.chooseAudio() }.keyboardShortcut("o")
            }
            CommandGroup(replacing: .saveItem) {
                Button("Save Sermon") { model.save() }.keyboardShortcut("s")
            }
            CommandGroup(after: .saveItem) {
                Menu("Export Transcript") { ForEach(ExportFormat.transcript) { format in Button(format.rawValue) { model.export(format, outline: false) } } }
                Menu("Export Outline") { ForEach(ExportFormat.outline) { format in Button(format.rawValue) { model.export(format, outline: true) } } }
                Divider()
                Button("Back Up Library…") { model.backup() }.keyboardShortcut("b", modifiers: [.command, .shift])
                Button("Restore or Import Archive…") { model.restore() }
            }
            CommandGroup(replacing: .printItem) {
                Button("Print Outline…") { model.printDocument(outline: true) }.keyboardShortcut("p")
                Button("Print Transcript…") { model.printDocument(outline: false) }.keyboardShortcut("p", modifiers: [.command, .shift])
            }
            CommandGroup(after: .appInfo) {
                if let controller = StudioUpdates.shared.controller { Button("Check for Updates…") { controller.checkForUpdates(nil) } }
            }
            CommandMenu("Sermon") {
                ForEach(Array(Page.allCases.enumerated()), id: \.offset) { index, page in
                    Button(page.rawValue) { if page == .newSermon { model.newSermon() } else { model.outlineEditor.flush(); model.page = page } }
                        .keyboardShortcut(KeyEquivalent(Character(String(index + 1))), modifiers: .command)
                }
                Divider()
                Button(model.focusMode ? "Exit Focus Mode" : "Focus Mode") { model.focusMode.toggle() }.keyboardShortcut("f", modifiers: [.command, .shift])
                Button("Transcribe Recording") { model.transcribe() }.keyboardShortcut("t", modifiers: [.command, .shift])
                Button("Generate Outline") { model.generateOutline() }.keyboardShortcut("g", modifiers: [.command, .shift])
                Button("Play or Pause Recording") { model.audio.togglePlay() }.keyboardShortcut(.space, modifiers: .option)
            }
        }
        Settings {
            SettingsView(model: model, transcriber: model.transcriber).frame(width: 820, height: 680)
        }
    }
}
