package org.pdfhelper.app

import com.google.gson.*
import com.itextpdf.forms.PdfAcroForm
import com.itextpdf.forms.PdfPageFormCopier
import com.itextpdf.forms.fields.PdfFormField
import com.itextpdf.io.font.PdfEncodings
import com.itextpdf.io.image.ImageDataFactory
import com.itextpdf.kernel.font.PdfFontFactory
import com.itextpdf.kernel.pdf.*
import com.itextpdf.kernel.pdf.canvas.PdfCanvas
import com.itextpdf.layout.Canvas
import com.itextpdf.layout.element.Paragraph
import com.itextpdf.layout.properties.TextAlignment
import com.itextpdf.layout.properties.VerticalAlignment
import java.io.File

class PdfEngine(private val defaultFont: ByteArray) {
    data class FieldInfo(val name: String, val type: String, val value: String, val states: List<String>)
    data class Placement(val page: Int, val x: Float, val y: Float, val width: Float, val height: Float, val path: String, val fit: Int, val anchor: Int?)
    fun listFields(template: File): List<FieldInfo> = PdfDocument(PdfReader(template)).use { pdf ->
        PdfAcroForm.getAcroForm(pdf, false)?.allFormFields?.toSortedMap()?.map { (name, field) ->
            FieldInfo(name, field.formType?.value ?: "?", field.valueAsString ?: "", field.appearanceStates?.toList() ?: emptyList())
        } ?: emptyList()
    }
    fun starter(template: File): JsonObject {
        val fields = JsonObject(); val checks = JsonObject()
        listFields(template).forEach { if (it.type == "Btn") checks.addProperty(it.name, false) else fields.addProperty(it.name, "") }
        return JsonObject().apply {
            addProperty("protocolVersion", 1); addProperty("template", "template.pdf")
            add("request", JsonObject().apply {
                add("fields", fields); add("checkboxes", checks)
                addProperty("flatten", true); addProperty("strict", true); addProperty("defaultFontSize", 0)
            })
        }
    }
    fun fill(job: JobArchive, output: File): List<String> {
        val warnings = mutableListOf<String>()
        if (job.requests.size == 1) fillOne(job, job.requests[0], output, warnings)
        else {
            PdfDocument(PdfWriter(output)).use { merged ->
                metadata(merged)
                job.requests.forEachIndexed { index, request ->
                    // These output names are generated internally, never read from job.json.
                    val part = File.createTempFile("part-$index-", ".pdf", job.directory)
                    try {
                        fillOne(job, request, part, warnings, "request${index + 1}_")
                        PdfDocument(PdfReader(part)).use { source ->
                            source.copyPagesTo(1, source.numberOfPages, merged, PdfPageFormCopier())
                        }
                    } finally { part.delete() }
                }
            }
        }
        return warnings
    }
    private fun enum(value: JsonElement?, names: List<String>, fallback: Int): Int {
        if (value == null || value.isJsonNull) return fallback
        val result = if (value.asJsonPrimitive.isNumber) value.asInt else names.indexOfFirst { it.equals(value.asString, true) }
        if (result !in names.indices) throw JobException("invalid_job", "Unknown enum value: $value")
        return result
    }
    private fun fillOne(job: JobArchive, request: JsonObject, output: File, warnings: MutableList<String>, prefix: String? = null) {
        val strict = request.bool("strict", true)
        fun missing(message: String) { if (strict) throw JobException("missing_field", message) else warnings.add(message) }
        PdfDocument(PdfReader(job.template), PdfWriter(output)).use { pdf ->
            metadata(pdf)
            val fontBytes = request.string("fontPath")?.takeIf { it.isNotBlank() }?.let { job.resolve(it).readBytes() } ?: defaultFont
            val font = PdfFontFactory.createFont(fontBytes, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED)
            val form = PdfAcroForm.getAcroForm(pdf, false)
            form?.setGenerateAppearance(true)
            val fields = form?.allFormFields ?: emptyMap()
            fun field(name: String): PdfFormField? = fields[name].also { if (it == null) missing("Template has no field '$name'.") }
            fun value(key: String): String? = request.obj("values")?.get(key)?.takeUnless { it.isJsonNull }?.asString
            fun setText(target: PdfFormField, text: String) {
                if (target.formType == PdfName.Tx || target.formType == PdfName.Ch) {
                    val size = target.fontSize.takeIf { it > 0 } ?: request.number("defaultFontSize")
                    target.setValue(text, font, size)
                } else target.setValue(text)
            }
            val pending = mutableListOf<Placement>()
            request.obj("imageFields")?.entrySet()?.forEach { (name, path) ->
                val f = field(name) ?: return@forEach
                if (f.widgets.isEmpty()) missing("Field '$name' has no widget.")
                f.widgets.forEach { widget ->
                    val page = widget.page
                    val rect = widget.rectangle?.toRectangle()
                    if (page == null || rect == null) missing("Field '$name' widget has no page or rectangle.")
                    else pending.add(Placement(pdf.getPageNumber(page), rect.x, rect.y, rect.width, rect.height, path.asString, 0, null))
                }
            }
            request.array("images").forEach { item ->
                val img = item.asJsonObject
                val path = img.string("path")
                if (path.isNullOrBlank()) { missing("Image has no path."); return@forEach }
                pending.add(Placement(img.number("page", 1f).toInt().coerceAtLeast(1), img.number("x"), img.number("y"), img.number("width"), img.number("height"), path,
                    enum(img.member("fit"), listOf("Contain", "Stretch"), 0), img.member("anchor")?.let { enum(it, listOf("BottomLeft", "BottomRight", "TopLeft", "TopRight"), 0) }))
            }
            val direct = request.obj("fields")
            if (direct != null && direct.size() > 0) direct.entrySet().forEach { (name, text) -> field(name)?.let { setText(it, if (text.isJsonNull) "" else text.asString) } }
            else request.obj("fieldMap")?.entrySet()?.forEach { (key, name) ->
                val text = value(key)
                if (text == null) missing("Missing value '$key'.")
                field(name.asString)?.let { setText(it, text ?: "") }
            }
            request.obj("checkboxes")?.entrySet()?.forEach { (name, checked) ->
                field(name)?.let { f ->
                    val on = f.appearanceStates?.firstOrNull { !it.equals("Off", true) } ?: "Yes"
                    f.setValue(if (checked.asBoolean) on else "Off")
                }
            }
            if (request.bool("flatten", true)) form?.flattenFields()
            else if (prefix != null) form?.rootFormFields?.keys?.toList()?.forEach { name -> form.renameField(name, prefix + name) }
            fun page(number: Int): PdfPage {
                if (number !in 1..pdf.numberOfPages) throw JobException("invalid_page", "Page $number exceeds template page count ${pdf.numberOfPages}.")
                return pdf.getPage(number)
            }
            request.array("stamps").forEach { item ->
                val stamp = item.asJsonObject
                val n = stamp.number("page", 1f).toInt().coerceAtLeast(1)
                val key = stamp.string("valueKey")
                val text = if (key == null) stamp.string("text", "") else value(key)
                if (text == null && key != null) missing("Missing stamp value '$key'.")
                val p = page(n)
                Canvas(PdfCanvas(p.newContentStreamAfter(), p.resources, pdf), p.pageSize).use { canvas ->
                    canvas.setFont(font).setFontSize(stamp.number("fontSize", 10f).takeIf { it > 0 } ?: 10f)
                    val alignment = when (enum(stamp.member("align"), listOf("Left", "Center", "Right"), 0)) { 1 -> TextAlignment.CENTER; 2 -> TextAlignment.RIGHT; else -> TextAlignment.LEFT }
                    canvas.showTextAligned(Paragraph(text ?: "").setMargin(0f), stamp.number("x"), stamp.number("y"), n, alignment, VerticalAlignment.BOTTOM, 0f)
                }
            }
            pending.forEach { image ->
                val p = page(image.page)
                val data = ImageDataFactory.create(job.resolve(image.path).absolutePath)
                val box = imageGeometry(image, data.width, data.height, p.pageSize.width, p.pageSize.height)
                PdfCanvas(p.newContentStreamAfter(), p.resources, pdf).addImageWithTransformationMatrix(data, box[2], 0f, 0f, box[3], box[0], box[1], false)
            }
        }
    }
    companion object {
        private fun metadata(pdf: PdfDocument) {
            pdf.documentInfo.setCreator("PDF Template Helper")
            pdf.documentInfo.setMoreInfo("PDFHelperLicense", "AGPL-3.0-or-later")
            pdf.documentInfo.setMoreInfo("PDFHelperLibrary", "iText Core Android; Copyright (c) iText Group NV; AGPL-3.0-or-later; https://itextpdf.com")
        }
        /** x,y,w,h geometry matches the .NET helper in PDF points, before page rotation. */
        fun imageGeometry(p: Placement, iw: Float, ih: Float, pw: Float, ph: Float): FloatArray {
            val tw = when { p.width > 0 -> p.width; p.height > 0 -> iw * p.height / ih; else -> iw }
            val th = when { p.height > 0 -> p.height; p.width > 0 -> ih * p.width / iw; else -> ih }
            val x = if (p.anchor == 1 || p.anchor == 3) pw - p.x - tw else if (p.anchor == null && p.x < 0) pw + p.x - tw else p.x
            val y = if (p.anchor == 2 || p.anchor == 3) ph - p.y - th else if (p.anchor == null && p.y < 0) ph + p.y - th else p.y
            if (p.fit == 1) return floatArrayOf(x, y, tw, th)
            val scale = minOf(tw / iw, th / ih)
            val dw = iw * scale; val dh = ih * scale
            return floatArrayOf(x + (tw - dw) / 2, y + (th - dh) / 2, dw, dh)
        }
    }
}
