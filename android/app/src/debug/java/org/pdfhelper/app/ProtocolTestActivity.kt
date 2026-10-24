package org.pdfhelper.app

import android.app.Activity
import android.content.Intent
import java.util.concurrent.CountDownLatch

/** Instrumentation-only result receiver, absent from release builds. */
class ProtocolTestActivity : Activity() {
    companion object {
        @Volatile var resultCodeReceived: Int? = null
        @Volatile var resultReceived: Intent? = null
        var completed = CountDownLatch(1)
        fun reset() { resultCodeReceived = null; resultReceived = null; completed = CountDownLatch(1) }
    }
    @Deprecated("Activity result contract")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        resultCodeReceived = resultCode; resultReceived = data; completed.countDown()
    }
}
