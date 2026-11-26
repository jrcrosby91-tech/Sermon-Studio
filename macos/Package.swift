// swift-tools-version: 5.9
import PackageDescription
let package = Package(
    name: "SermonStudio",
    platforms: [.macOS(.v14)],
    dependencies: [.package(url: "https://github.com/sparkle-project/Sparkle.git", exact: "2.7.1"), .package(url: "https://github.com/argmaxinc/WhisperKit.git", exact: "0.9.0")],
    targets: [
        // Platform-independent sermon logic shared by the app and its tests.
        .target(name: "SermonCore"),
        .executableTarget(name: "SermonStudio", dependencies: ["SermonCore", .product(name: "Sparkle", package: "Sparkle"), .product(name: "WhisperKit", package: "WhisperKit")]),
        .testTarget(name: "SermonCoreTests", dependencies: ["SermonCore"], resources: [.copy("Fixtures")]),
    ]
)
