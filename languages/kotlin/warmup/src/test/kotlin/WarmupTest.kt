package challenge

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.test.runTest

class WarmupTest {
    @Test
    fun addsAfterSuspending() = runTest {
        assertEquals(5, addLater(2, 3))
    }
}
