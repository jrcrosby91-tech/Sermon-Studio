// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "SermonStudio", platforms: [.macOS(.v14)], dependencies: [.package(url: "https://github.com/argmaxinc/WhisperKit.git", exact: "0.9.0")], targets: [.executableTarget(name: "SermonStudio", dependencies: [.product(name: "WhisperKit", package: "WhisperKit")])])
