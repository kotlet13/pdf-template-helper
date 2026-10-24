package org.pdfhelper.app

import android.app.Activity
import android.content.ClipData
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.*
import androidx.core.content.FileProvider
import com.google.gson.Gson
import com.google.gson.GsonBuilder
import com.google.gson.JsonParser
import java.io.File
import java.util.UUID
import java.util.concurrent.Executors
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

internal object Runtime {
    val worker = Executors.newSingleThreadExecutor()
    fun engine(activity: Activity) = PdfEngine(activity.assets.open("fonts/NotoSans-Regular.ttf").use { it.readBytes() })
    fun cleanup(activity: Activity) {
        val threshold = System.currentTimeMillis() - 24 * 60 * 60 * 1000
        listOf("results", "jobs", "manual").forEach { name ->
            File(activity.cacheDir, name).listFiles()?.filter { it.lastModified() < threshold }?.forEach { it.deleteRecursively() }
        }
    }
    fun resultFile(activity: Activity, suffix: String = ".pdf") = File(File(activity.cacheDir, "results").apply { mkdirs() }, UUID.randomUUID().toString() + suffix)
    fun uri(activity: Activity, file: File): Uri = FileProvider.getUriForFile(activity, "org.pdfhelper.app.files", file)
    fun fill(activity: Activity, uri: Uri): Pair<File, List<String>> {
        if (uri.scheme != "content") throw JobException("invalid_uri", "Input must be a content URI.")
        val output = resultFile(activity)
        try {
            val input = activity.contentResolver.openInputStream(uri) ?: throw JobException("invalid_uri", "Cannot read job.")
            JobArchive.open(input, File(activity.cacheDir, "jobs")).use { archive ->
                return output to engine(activity).fill(archive, output)
            }
        } catch (e: Exception) { output.delete(); throw e }
    }
}

class CapabilitiesActivity : Activity() {
    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        setResult(RESULT_OK, Intent().putExtra("protocolVersion", 1).putExtra("status", "ok")
            .putExtra("operations", arrayOf("fill", "batch", "list-fields", "new-job")))
        finish()
    }
}

/** Exported protocol endpoint: the caller explicitly launches this action for result. */
class FillActivity : Activity() {
    private var canceled = false
    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        setResult(RESULT_CANCELED)
        if (state != null) { finish(); return }
        val layout = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(32, 48, 32, 32) }
        layout.addView(TextView(this).apply { text = "PDF Template Helper\nFilling PDF on this device…"; textSize = 20f })
        layout.addView(ProgressBar(this))
        layout.addView(Button(this).apply { text = "Cancel"; setOnClickListener { canceled = true; finish() } })
        layout.setOnApplyWindowInsetsListener { view, insets ->
            view.setPadding(32 + insets.systemWindowInsetLeft, 48 + insets.systemWindowInsetTop, 32 + insets.systemWindowInsetRight, 32 + insets.systemWindowInsetBottom)
            insets
        }
        setContentView(layout)
        // Keep this short-lived result Activity intact while the worker runs.
        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LOCKED
        Runtime.worker.execute {
            var output: File? = null
            try {
                Runtime.cleanup(this)
                if (intent.action != "org.pdfhelper.action.FILL") throw JobException("invalid_action", "Unsupported action.")
                if (intent.getIntExtra("protocolVersion", -1) != 1) throw JobException("unsupported_protocol", "protocolVersion extra must be 1.")
                val input = intent.data ?: throw JobException("invalid_uri", "Missing job URI.")
                val (pdf, warnings) = Runtime.fill(this, input)
                output = pdf
                val uri = Runtime.uri(this, pdf)
                runOnUiThread {
                    if (canceled || isFinishing || isDestroyed) pdf.delete()
                    else {
                        setResult(RESULT_OK, Intent().setDataAndType(uri, "application/pdf")
                            .apply { clipData = ClipData.newRawUri("Filled PDF", uri) }
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                            .putExtra("protocolVersion", 1).putExtra("status", "ok").putExtra("warnings", Gson().toJson(warnings)))
                        finish()
                    }
                }
            } catch (e: Exception) {
                output?.delete()
                runOnUiThread {
                    if (!canceled && !isFinishing && !isDestroyed) {
                        setResult(RESULT_FIRST_USER, Intent().putExtra("protocolVersion", 1).putExtra("status", "error")
                            .putExtra("code", (e as? JobException)?.code ?: "fill_failed")
                            .putExtra("message", e.message ?: "PDF generation failed."))
                        finish()
                    }
                }
            }
        }
    }
}

/** Standalone, offline workflow: select a template, edit JSON, save a reusable job or fill it. */
class MainActivity : Activity() {
    private lateinit var status: TextView
    private lateinit var editor: EditText
    private lateinit var templateLabel: TextView
    private lateinit var controls: LinearLayout
    private var template: File? = null
    private var generated: File? = null
    private var pendingExport: File? = null
    private var busy = false
    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        Runtime.cleanup(this)
        val scroll = ScrollView(this)
        scroll.setOnApplyWindowInsetsListener { view, insets ->
            view.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom)
            insets
        }
        controls = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(24, 36, 24, 36) }
        scroll.addView(controls)
        controls.addView(TextView(this).apply { text = "PDF Template Helper"; textSize = 25f })
        controls.addView(TextView(this).apply { text = "Fill PDF forms locally. Import a .pdfjob, or select any PDF template to inspect its fields and create a reusable job. No account or network required." })
        button("Import and fill .pdfjob") { pick(10, arrayOf("application/zip", "application/octet-stream", "*/*")) }
        button("Select PDF template / list fields") { pick(11, arrayOf("application/pdf")) }
        templateLabel = TextView(this).also { it.text = "No template selected"; controls.addView(it) }
        editor = EditText(this).apply {
            hint = "Starter job JSON appears here after selecting a template. Edit field values before saving or filling."
            typeface = android.graphics.Typeface.MONOSPACE; textSize = 13f
            gravity = android.view.Gravity.TOP; minLines = 12
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE or android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
        }
        controls.addView(editor)
        button("Fill edited job") { buildAndFill() }
        button("Save .pdfjob") { exportJob() }
        button("Save starter JSON") {
            val json = editor.text.toString()
            runCatching { JsonParser.parseString(json).asJsonObject }.onFailure { showError(it); return@button }
            val file = Runtime.resultFile(this, ".json").apply { writeText(json, Charsets.UTF_8) }
            save(file, "application/json", "job.json")
        }
        button("Save filled PDF") { generated?.let { save(it, "application/pdf", "filled.pdf") } ?: showError(IllegalStateException("Fill a job first.")) }
        button("Open filled PDF") {
            generated?.let {
                val uri = Runtime.uri(this, it)
                runCatching { startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/pdf").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)) }.onFailure(::showError)
            } ?: showError(IllegalStateException("Fill a job first."))
        }
        button("About / licenses / source") { showLicenses() }
        status = TextView(this).also { controls.addView(it) }
        controls.addView(TextView(this).apply { text = "Protocol v1 • AGPL-3.0-or-later • iText Android\nDefault font: Noto Sans (SIL OFL license bundled). Images/fonts referenced by JSON must be included inside imported .pdfjob archives. Template-only editing supports fields, checkboxes and text stamps."; textSize = 12f })
        setContentView(scroll)
        state?.getString("template")?.let { template = File(it).takeIf(File::isFile); templateLabel.text = template?.name ?: "No template selected" }
        state?.getString("generated")?.let { generated = File(it).takeIf(File::isFile) }
        state?.getString("export")?.let { pendingExport = File(it).takeIf(File::isFile) }
        state?.getString("json")?.let { editor.setText(it) }
    }
    private fun button(label: String, action: () -> Unit) { controls.addView(Button(this).apply { text = label; setOnClickListener { if (!busy) action() } }) }
    private fun pick(code: Int, types: Array<String>) {
        startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).setType(if (types.size == 1) types[0] else "*/*")
            .addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_MIME_TYPES, types).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION), code)
    }
    private fun showError(error: Throwable) { status.text = "Error: ${error.message ?: error.javaClass.simpleName}" }
    private fun showLicenses() {
        val version = packageManager.getPackageInfo(packageName, 0).versionName
        val text = buildString {
            append("PDF Template Helper $version\nProtocol v1 • Android 8.1+\n\n")
            append("Corresponding source and build instructions:\nhttps://github.com/kotlet13/pdf-template-helper\n\n")
            append("iText Core Android 9.2.0 • Copyright (c) iText Group NV\nhttps://github.com/itext/itext-java\nhttps://itextpdf.com/how-buy/AGPLv3-license\n\n")
            for (name in listOf("LICENSE", "LICENSE-itext.md", "THIRD_PARTY_NOTICES.md")) {
                append("\n===== $name =====\n\n")
                append(assets.open("licenses/$name").bufferedReader(Charsets.UTF_8).use { it.readText() })
            }
            append("\n===== Noto Sans OFL =====\n\n")
            append(assets.open("fonts/OFL.txt").bufferedReader(Charsets.UTF_8).use { it.readText() })
        }
        val content = TextView(this).apply {
            this.text = text; textSize = 13f; setPadding(24, 16, 24, 16)
            setTextIsSelectable(true)
            android.text.util.Linkify.addLinks(this, android.text.util.Linkify.WEB_URLS)
            movementMethod = android.text.method.LinkMovementMethod.getInstance()
        }
        android.app.AlertDialog.Builder(this).setTitle("About / licenses / source")
            .setView(ScrollView(this).apply { addView(content) }).setPositiveButton("Close", null).show()
    }
    private fun <T> work(message: String, task: () -> T, done: (T) -> Unit) {
        busy = true; status.text = message
        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LOCKED
        Runtime.worker.execute {
            val result = runCatching(task)
            runOnUiThread {
                busy = false
                requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
                if (!isDestroyed) result.onSuccess(done).onFailure(::showError)
            }
        }
    }
    private fun save(file: File, mime: String, name: String) {
        pendingExport = file
        startActivityForResult(Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType(mime).putExtra(Intent.EXTRA_TITLE, name), 12)
    }
    private fun createJob(pdf: File, json: String): File {
        val parsed = JsonParser.parseString(json).asJsonObject
        parsed.addProperty("template", "template.pdf")
        val out = Runtime.resultFile(this, ".pdfjob")
        try {
            ZipOutputStream(out.outputStream()).use { zip ->
                zip.putNextEntry(ZipEntry("job.json")); zip.write(GsonBuilder().setPrettyPrinting().create().toJson(parsed).toByteArray(Charsets.UTF_8)); zip.closeEntry()
                zip.putNextEntry(ZipEntry("template.pdf")); pdf.inputStream().use { it.copyTo(zip) }; zip.closeEntry()
            }
            // Validate all references before offering an export.
            JobArchive.open(out.inputStream(), File(cacheDir, "jobs")).use { }
            return out
        } catch (e: Exception) { out.delete(); throw e }
    }
    private fun buildAndFill() {
        val pdf = template ?: run { showError(IllegalStateException("Select a template first.")); return }
        val json = editor.text.toString()
        work("Filling PDF…", {
            val archive = createJob(pdf, json)
            val output = Runtime.resultFile(this)
            try {
                val warnings = JobArchive.open(archive.inputStream(), File(cacheDir, "jobs")).use { Runtime.engine(this).fill(it, output) }
                output to warnings
            } catch (e: Exception) { output.delete(); throw e } finally { archive.delete() }
        }) { (output, warnings) -> generated = output; status.text = "PDF ready. Use Save filled PDF.\n${warnings.joinToString("\n")}" }
    }
    private fun exportJob() {
        val pdf = template ?: run { showError(IllegalStateException("Select a template first.")); return }
        val json = editor.text.toString()
        work("Preparing job…", { createJob(pdf, json) }) { status.text = "Job ready."; save(it, "application/zip", "template.pdfjob") }
    }
    @Deprecated("Legacy callback required for minimum supported Android")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK) return
        val uri = data?.data ?: return
        when (requestCode) {
            10 -> work("Importing and filling job…", { Runtime.fill(this, uri) }) { (pdf, warnings) -> generated = pdf; status.text = "PDF ready. Use Save filled PDF.\n${warnings.joinToString("\n")}" }
            11 -> work("Reading PDF fields…", {
                val directory = File(cacheDir, "manual").apply { mkdirs() }
                val pdf = File.createTempFile("template-", ".pdf", directory)
                try {
                    contentResolver.openInputStream(uri)!!.use { input -> pdf.outputStream().use { out ->
                        val buffer = ByteArray(16384); var total = 0L
                        while (true) { val n = input.read(buffer); if (n < 0) break; total += n; if (total > JobArchive.MAX_ENTRY) throw JobException("archive_limit", "Template exceeds 50 MiB."); out.write(buffer, 0, n) }
                    } }
                    val engine = Runtime.engine(this)
                    Triple(pdf, engine.listFields(pdf), GsonBuilder().setPrettyPrinting().create().toJson(engine.starter(pdf)))
                } catch (e: Exception) { pdf.delete(); throw e }
            }) { (pdf, fields, json) ->
                template?.delete(); template = pdf; templateLabel.text = "Template loaded • ${fields.size} fields"
                editor.setText(json)
                status.text = if (fields.isEmpty()) "No AcroForm fields. Add coordinate stamps to the request. Coordinates are PDF points from the bottom left."
                    else fields.joinToString("\n") { "${it.name} (${it.type}) ${it.states.joinToString()}" }
            }
            12 -> pendingExport?.let { source -> work("Saving…", {
                contentResolver.openOutputStream(uri, "w")?.use { out -> source.inputStream().use { it.copyTo(out) } }
                    ?: throw JobException("save_failed", "Cannot open destination.")
            }) { status.text = "Saved."; pendingExport = null } }
        }
    }
    override fun onSaveInstanceState(outState: Bundle) {
        outState.putString("template", template?.absolutePath); outState.putString("generated", generated?.absolutePath)
        outState.putString("export", pendingExport?.absolutePath); outState.putString("json", editor.text.toString())
        super.onSaveInstanceState(outState)
    }
}
