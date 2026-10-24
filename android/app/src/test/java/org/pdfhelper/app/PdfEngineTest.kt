package org.pdfhelper.app

import com.google.gson.JsonParser
import com.itextpdf.forms.PdfAcroForm
import com.itextpdf.forms.fields.CheckBoxFormFieldBuilder
import com.itextpdf.forms.fields.TextFormFieldBuilder
import com.itextpdf.kernel.geom.Rectangle
import com.itextpdf.kernel.pdf.*
import com.itextpdf.kernel.pdf.canvas.parser.PdfTextExtractor
import org.junit.Assert.*
import org.junit.Test
import java.io.*
import java.util.zip.*

class PdfEngineTest {
    private val font get() = File("src/main/assets/fonts/NotoSans-Regular.ttf").readBytes()
    private fun temp(block: (File) -> Unit) {
        val dir = kotlin.io.path.createTempDirectory("pdfhelper-test").toFile()
        try { block(dir) } finally { dir.deleteRecursively() }
    }
    private fun template(file: File) {
        PdfDocument(PdfWriter(file)).use { pdf ->
            val page = pdf.addNewPage()
            val form = PdfAcroForm.getAcroForm(pdf, true)
            form.addField(TextFormFieldBuilder(pdf, "customer.name").setWidgetRectangle(Rectangle(40f, 700f, 300f, 30f)).createText(), page)
            form.addField(CheckBoxFormFieldBuilder(pdf, "approved").setWidgetRectangle(Rectangle(40f, 650f, 20f, 20f)).createCheckBox(), page)
        }
    }
    private fun zip(vararg entries: Pair<String, ByteArray>): ByteArray = ByteArrayOutputStream().apply {
        ZipOutputStream(this).use { out -> entries.forEach { (name, bytes) -> out.putNextEntry(ZipEntry(name)); out.write(bytes); out.closeEntry() } }
    }.toByteArray()
    @Test fun nestedFieldsUnicodeCheckboxFlattenAndBatch() = temp { dir ->
        val template = File(dir, "source.pdf"); template(template)
        val manifest = """{"protocolVersion":1,"template":"template.pdf","requests":[{"fields":{"customer.name":"Žiga Čšćđž"},"checkboxes":{"approved":true},"stamps":[{"text":"First report","x":50,"y":600}]},{"fields":{"customer.name":"Second customer"}}]}"""
        val archive = zip("job.json" to manifest.toByteArray(), "template.pdf" to template.readBytes())
        JobArchive.open(archive.inputStream(), File(dir, "jobs")).use { job ->
            val engine = PdfEngine(font)
            assertTrue(engine.listFields(job.template).any { it.name == "customer.name" })
            val result = File(dir, "filled.pdf")
            assertEquals(emptyList<String>(), engine.fill(job, result))
            PdfDocument(PdfReader(result)).use { pdf ->
                assertEquals(2, pdf.numberOfPages)
                assertTrue(PdfTextExtractor.getTextFromPage(pdf.getPage(1)).contains("Žiga Čšćđž"))
                assertTrue(PdfTextExtractor.getTextFromPage(pdf.getPage(1)).contains("First report"))
                assertTrue(PdfTextExtractor.getTextFromPage(pdf.getPage(2)).contains("Second customer"))
                assertTrue(PdfAcroForm.getAcroForm(pdf, false)?.allFormFields.isNullOrEmpty())
            }
        }
        assertTrue(File(dir, "jobs").listFiles()!!.isEmpty())
    }
    @Test fun strictAndLenientMissingField() = temp { dir ->
        val template = File(dir, "source.pdf"); template(template)
        for (strict in listOf(true, false)) {
            val manifest = """{"protocolVersion":1,"template":"template.pdf","request":{"fields":{"missing":"x"},"strict":$strict}}"""
            JobArchive.open(zip("job.json" to manifest.toByteArray(), "template.pdf" to template.readBytes()).inputStream(), File(dir, "jobs")).use { job ->
                if (strict) {
                    val error = assertThrows(JobException::class.java) { PdfEngine(font).fill(job, File(dir, "out.pdf")) }
                    assertEquals("missing_field", error.code)
                } else assertEquals(1, PdfEngine(font).fill(job, File(dir, "out.pdf")).size)
            }
        }
    }
    @Test fun rejectsTraversalAbsoluteExternalAssetsAndCleansUp() = temp { dir ->
        val jobs = File(dir, "jobs")
        for (path in listOf("../escape", "/root.pdf", "C:/secret", "assets/../../secret", "assets\\secret", "assets/CON.png", "assets/image.png.", "assets/image.png ", "assets/a?b", "a".repeat(241))) {
            assertThrows(JobException::class.java) { JobArchive.open(zip(path to byteArrayOf(1)).inputStream(), jobs) }
        }
        val manifest = """{"protocolVersion":1,"template":"template.pdf","request":{"fontPath":"../secret.ttf","strict":false}}"""
        assertThrows(JobException::class.java) { JobArchive.open(zip("job.json" to manifest.toByteArray(), "template.pdf" to byteArrayOf(1)).inputStream(), jobs) }
        assertTrue(jobs.listFiles()!!.isEmpty())
    }
    @Test fun rejectsDuplicateNamesAndArchiveLimits() = temp { dir ->
        val jobs = File(dir, "jobs")
        assertThrows(JobException::class.java) { JobArchive.open(zip("A.pdf" to byteArrayOf(1), "a.pdf" to byteArrayOf(2)).inputStream(), jobs) }
        val entries = (1..257).map { "entry$it" to byteArrayOf(1) }.toTypedArray()
        assertEquals("archive_limit", assertThrows(JobException::class.java) { JobArchive.open(zip(*entries).inputStream(), jobs) }.code)
        assertThrows(JobException::class.java) { JobArchive.open(zip("job.json" to ByteArray(1024 * 1024 + 1) { 32 }).inputStream(), jobs) }
        assertTrue(jobs.listFiles()!!.isEmpty())
    }
    @Test fun starterHasExactNestedNamesAndDefaults() = temp { dir ->
        val source = File(dir, "source.pdf"); template(source)
        val starter = PdfEngine(font).starter(source)
        assertTrue(starter.getAsJsonObject("request").getAsJsonObject("fields").has("customer.name"))
        assertFalse(starter.getAsJsonObject("request").getAsJsonObject("checkboxes").get("approved").asBoolean)
    }
    @Test fun sharedProtocolFixturesFillWithoutRasterization() = temp { dir ->
        for (name in listOf("sample", "sample-editable", "sample-batch")) {
            val fixture = File("../../examples/$name.pdfjob")
            assertTrue("Shared fixture missing: $fixture", fixture.isFile)
            JobArchive.open(fixture.inputStream(), File(dir, "jobs")).use { job ->
                val out = File(dir, "$name.pdf")
                PdfEngine(font).fill(job, out)
                PdfDocument(PdfReader(out)).use { pdf ->
                    assertEquals(if (name == "sample-batch") 2 else 1, pdf.numberOfPages)
                    if (name == "sample-editable") assertTrue(PdfAcroForm.getAcroForm(pdf, false).getField("customer").valueAsString.contains("Čebelica"))
                    else assertTrue("$name: " + PdfTextExtractor.getTextFromPage(pdf.getPage(1)), PdfTextExtractor.getTextFromPage(pdf.getPage(1)).contains("Čebelica"))
                    assertTrue(pdf.documentInfo.producer.contains("iText"))
                    if (name != "sample-editable") assertTrue(PdfAcroForm.getAcroForm(pdf, false)?.allFormFields.isNullOrEmpty())
                }
            }
        }
    }
    @Test fun editableBatchPreservesIndependentFields() = temp { dir ->
        val source = File(dir, "source.pdf"); template(source)
        val manifest = """{"protocolVersion":1,"template":"template.pdf","requests":[{"fields":{"customer.name":"First"},"flatten":false},{"fields":{"customer.name":"Second"},"flatten":false}]}"""
        JobArchive.open(zip("job.json" to manifest.toByteArray(), "template.pdf" to source.readBytes()).inputStream(), File(dir, "jobs")).use { job ->
            val output = File(dir, "out.pdf"); PdfEngine(font).fill(job, output)
            PdfDocument(PdfReader(output)).use { pdf ->
                val form = PdfAcroForm.getAcroForm(pdf, false)
                assertEquals("First", form.getField("request1_customer.name").valueAsString)
                assertEquals("Second", form.getField("request2_customer.name").valueAsString)
            }
        }
    }
    @Test fun geometryMatchesAnchorsContainStretchAndNegativeMargins() {
        fun place(x: Float, y: Float, w: Float, h: Float, fit: Int, anchor: Int?) = PdfEngine.Placement(1, x, y, w, h, "x.png", fit, anchor)
        assertArrayEquals(floatArrayOf(380f, 745f, 200f, 100f), PdfEngine.imageGeometry(place(20f, 30f, 200f, 150f, 0, 3), 400f, 200f, 600f, 900f), .001f)
        assertArrayEquals(floatArrayOf(380f, 720f, 200f, 150f), PdfEngine.imageGeometry(place(-20f, -30f, 200f, 150f, 1, null), 400f, 200f, 600f, 900f), .001f)
        assertArrayEquals(floatArrayOf(10f, 20f, 100f, 50f), PdfEngine.imageGeometry(place(10f, 20f, 100f, 0f, 0, 0), 400f, 200f, 600f, 900f), .001f)
    }
}
