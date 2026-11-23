package challenge

import kotlinx.coroutines.delay

suspend fun addLater(a: Int, b: Int): Int {
    delay(10)
    return a + b
}
