package org.pdfhelper.app

import android.app.Activity
import android.content.ClipData
import android.content.Intent
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.itextpdf.kernel.pdf.PdfDocument
import com.itextpdf.kernel.pdf.PdfReader
import com.itextpdf.kernel.pdf.PdfWriter
import com.itextpdf.kernel.pdf.canvas.parser.PdfTextExtractor
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.concurrent.TimeUnit
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

@RunWith(AndroidJUnit4::class)
class ProtocolInstrumentedTest {
    @Test fun capabilitiesAdvertisesGenericProtocol() {
        ProtocolTestActivity.reset()
        ActivityScenario.launch(ProtocolTestActivity::class.java).use { scenario ->
            scenario.onActivity { activity -> activity.startActivityForResult(Intent("org.pdfhelper.action.CAPABILITIES").setPackage("org.pdfhelper.app"), 1) }
            assertTrue(ProtocolTestActivity.completed.await(15, TimeUnit.SECONDS))
            assertEquals(Activity.RESULT_OK, ProtocolTestActivity.resultCodeReceived)
            val result = ProtocolTestActivity.resultReceived!!
            assertEquals(1, result.getIntExtra("protocolVersion", -1))
            assertTrue(result.getStringArrayExtra("operations")!!.toList().containsAll(listOf("fill", "batch", "list-fields", "new-job")))
        }
    }
    @Test fun actualFillReturnsReadablePdfAndGrant() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val template = File(context.cacheDir, "test-template.pdf")
        PdfDocument(PdfWriter(template)).use { it.addNewPage() }
        val job = File(File(context.cacheDir, "results").apply { mkdirs() }, "instrumentation.pdfjob")
        ZipOutputStream(job.outputStream()).use { zip ->
            zip.putNextEntry(ZipEntry("job.json")); zip.write("""{"protocolVersion":1,"template":"template.pdf","request":{"stamps":[{"text":"Žiga Čšćđž Android","x":40,"y":700}]}}""".toByteArray()); zip.closeEntry()
            zip.putNextEntry(ZipEntry("template.pdf")); zip.write(template.readBytes()); zip.closeEntry()
        }
        ProtocolTestActivity.reset()
        ActivityScenario.launch(ProtocolTestActivity::class.java).use { scenario ->
            scenario.onActivity { activity ->
                val uri = Runtime.uri(activity, job)
                activity.startActivityForResult(Intent("org.pdfhelper.action.FILL").setPackage("org.pdfhelper.app")
                    .setDataAndType(uri, "application/zip").apply { clipData = ClipData.newRawUri("PDF job", uri) }
                    .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION).putExtra("protocolVersion", 1), 1)
            }
            assertTrue(ProtocolTestActivity.completed.await(45, TimeUnit.SECONDS))
            val result = ProtocolTestActivity.resultReceived!!
            assertEquals(result.getStringExtra("message"), Activity.RESULT_OK, ProtocolTestActivity.resultCodeReceived)
            assertEquals("ok", result.getStringExtra("status"))
            assertEquals("[]", result.getStringExtra("warnings"))
            assertEquals(1, result.getIntExtra("protocolVersion", -1))
            assertTrue(result.flags and Intent.FLAG_GRANT_READ_URI_PERMISSION != 0)
            assertEquals(result.data, result.clipData!!.getItemAt(0).uri)
            context.contentResolver.openInputStream(result.data!!)!!.use { input ->
                PdfDocument(PdfReader(input)).use { pdf ->
                    assertTrue(PdfTextExtractor.getTextFromPage(pdf.getPage(1)).contains("Žiga Čšćđž Android"))
                    assertTrue(pdf.documentInfo.producer.contains("iText"))
                }
            }
        }
        template.delete(); job.delete()
    }
    @Test fun unsupportedProtocolReturnsStructuredError() {
        ProtocolTestActivity.reset()
        ActivityScenario.launch(ProtocolTestActivity::class.java).use { scenario ->
            scenario.onActivity { activity ->
                activity.startActivityForResult(Intent(activity, FillActivity::class.java).setAction("org.pdfhelper.action.FILL").putExtra("protocolVersion", 99), 1)
            }
            assertTrue(ProtocolTestActivity.completed.await(15, TimeUnit.SECONDS))
            assertEquals(Activity.RESULT_FIRST_USER, ProtocolTestActivity.resultCodeReceived)
            assertEquals("error", ProtocolTestActivity.resultReceived!!.getStringExtra("status"))
            assertEquals("unsupported_protocol", ProtocolTestActivity.resultReceived!!.getStringExtra("code"))
        }
    }
}
