package org.pdfhelper.app

import com.google.gson.*
import java.io.*
import java.util.UUID
import java.util.zip.ZipInputStream

class JobException(val code: String, message: String) : IOException(message)

internal fun JsonObject.member(name: String): JsonElement? = entrySet().firstOrNull { it.key.equals(name, true) }?.value?.takeUnless { it.isJsonNull }
internal fun JsonObject.obj(name: String) = member(name)?.asJsonObject
internal fun JsonObject.array(name: String) = member(name)?.asJsonArray ?: JsonArray()
internal fun JsonObject.string(name: String, fallback: String? = null): String? = member(name)?.asString ?: fallback
internal fun JsonObject.bool(name: String, fallback: Boolean) = member(name)?.asBoolean ?: fallback
internal fun JsonObject.number(name: String, fallback: Float = 0f): Float = (member(name)?.asFloat ?: fallback).also {
    if (!it.isFinite()) throw JobException("invalid_job", "$name must be finite.")
}
internal fun safeJobName(path: String): Boolean {
    if (path.isBlank() || path.length > 240 || path.startsWith('/') || path.any { it < ' ' || it in "\\:<>\"|?*" }) return false
    return path.split('/').all { segment ->
        segment.isNotEmpty() && segment != "." && segment != ".." && !segment.endsWith('.') && !segment.endsWith(' ') &&
            !segment.substringBefore('.').uppercase(java.util.Locale.ROOT).matches(Regex("CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]"))
    }
}

/** Every caller-controlled path is resolved here, including optional font and image paths. */
class JobArchive private constructor(val directory: File, val json: JsonObject) : Closeable {
    val template: File
    val requests: List<JsonObject>
    init {
        if (json.member("protocolVersion")?.asString != "1") throw JobException("unsupported_protocol", "Only protocolVersion 1 is supported.")
        template = resolve(json.string("template") ?: throw JobException("invalid_job", "Missing template."))
        val single = json.obj("request")
        val batch = json.member("requests")
        if ((single == null) == (batch == null)) throw JobException("invalid_job", "Provide request or requests, exclusively.")
        requests = single?.let { listOf(it) } ?: batch!!.asJsonArray.map { it.asJsonObject }
        if (requests.isEmpty() || requests.size > 100) throw JobException("invalid_job", "requests must contain 1–100 entries.")
        requests.forEach { request ->
            request.string("fontPath")?.takeIf { it.isNotBlank() }?.let(::resolve)
            request.obj("imageFields")?.entrySet()?.forEach { resolve(it.value.asString) }
            request.array("images").forEach { image -> image.asJsonObject.string("path")?.takeIf { it.isNotBlank() }?.let(::resolve) }
        }
    }
    fun resolve(path: String): File {
        if (!safeJobName(path)) {
            throw JobException("unsafe_path", "Paths must be contained relative paths: $path")
        }
        val file = File(directory, path).canonicalFile
        if (!file.toPath().startsWith(directory.canonicalFile.toPath()) || !file.isFile) throw JobException("missing_asset", "Job asset is missing: $path")
        return file
    }
    override fun close() { directory.deleteRecursively() }
    companion object {
        const val MAX_BYTES = 100L * 1024 * 1024
        const val MAX_ENTRY = 50L * 1024 * 1024
        fun open(input: InputStream, workParent: File): JobArchive {
            workParent.mkdirs()
            val directory = File(workParent, UUID.randomUUID().toString()).apply { mkdirs() }
            try {
                var bytes = 0L
                var count = 0
                val names = mutableSetOf<String>()
                ZipInputStream(BufferedInputStream(input)).use { zip ->
                    while (true) {
                        val entry = zip.nextEntry ?: break
                        val name = entry.name
                        if (++count > 256) throw JobException("archive_limit", "Job has more than 256 ZIP entries.")
                        val clean = name.removeSuffix("/")
                        if (!safeJobName(clean)) throw JobException("unsafe_path", "Unsafe ZIP path.")
                        if (!names.add(clean.lowercase(java.util.Locale.ROOT))) throw JobException("invalid_job", "Duplicate ZIP path.")
                        val destination = File(directory, clean).canonicalFile
                        if (!destination.toPath().startsWith(directory.canonicalFile.toPath())) throw JobException("unsafe_path", "ZIP escapes job directory.")
                        if (entry.isDirectory) { destination.mkdirs(); continue }
                        destination.parentFile!!.mkdirs()
                        var entryBytes = 0L
                        destination.outputStream().use { output ->
                            val buffer = ByteArray(16384)
                            while (true) {
                                val n = zip.read(buffer)
                                if (n == -1) break
                                bytes += n; entryBytes += n
                                if (bytes > MAX_BYTES || entryBytes > MAX_ENTRY) throw JobException("archive_limit", "Expanded job exceeds size limit.")
                                output.write(buffer, 0, n)
                            }
                        }
                    }
                }
                val manifest = File(directory, "job.json")
                if (!manifest.isFile || manifest.length() > 1024 * 1024) throw JobException("invalid_job", "Missing or oversized root job.json.")
                return JobArchive(directory, JsonParser.parseString(manifest.readText(Charsets.UTF_8)).asJsonObject)
            } catch (e: Exception) {
                directory.deleteRecursively()
                if (e is JobException) throw e
                throw JobException("invalid_job", e.message ?: "Invalid PDF job archive.")
            }
        }
    }
}
