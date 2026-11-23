// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Challenge",
    products: [
        .library(name: "Challenge", targets: ["Challenge"]),
    ],
    targets: [
        .target(
            name: "Challenge",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .testTarget(
            name: "ChallengeTests",
            dependencies: ["Challenge"],
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
    ]
)
