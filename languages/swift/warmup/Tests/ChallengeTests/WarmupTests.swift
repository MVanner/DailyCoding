import XCTest
@testable import Challenge

final class WarmupTests: XCTestCase {
    func testAdds() {
        XCTAssertEqual(addNumbers(2, 3), 5)
    }
}
