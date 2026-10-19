// Read-only stored-preview extractor. Container/schema references: wamsoft/clipparse,
// commit 322a273c0e059d1dee9132af78289efa12a59522 (MIT). No layer rendering or source writes.
#include "sqlite3.h"
#include <windows.h>
#include <fcntl.h>
#include <io.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <vector>

constexpr uint64_t PixelLimit = 256ull * 1024 * 1024;
constexpr uint64_t MetadataLimit = 256ull * 1024 * 1024;
constexpr uint64_t EncodedLimit = PixelLimit + 16ull * 1024 * 1024;
static void fail(const char* reason) { throw std::runtime_error(reason); }
static uint32_t be32(const uint8_t* p) { return uint32_t(p[0]) << 24 | uint32_t(p[1]) << 16 | uint32_t(p[2]) << 8 | p[3]; }
static uint64_t be64(const uint8_t* p) { return uint64_t(be32(p)) << 32 | be32(p + 4); }
static void read(void* output, size_t length) {
    auto p = static_cast<uint8_t*>(output);
    while (length) { const auto n = fread(p, 1, length, stdin); if (!n) fail("INVALID_TRUNCATED"); p += n; length -= n; }
}
static void skip(uint64_t length) {
    std::array<uint8_t, 65536> buffer{};
    while (length) { const auto n = static_cast<size_t>(length < buffer.size() ? length : buffer.size()); read(buffer.data(), n); length -= n; }
}
static std::vector<uint8_t> database() {
    std::array<uint8_t,24> header{}; read(header.data(), header.size());
    if (memcmp(header.data(), "CSFCHUNK", 8)) fail("INVALID_SIGNATURE");
    const auto total = be64(header.data() + 8);
    if (be64(header.data() + 16) != 24 || total < 96) fail("UNSUPPORTED_HEADER");
    uint64_t position = 24, sqlOffset = 0; bool head = false, footer = false;
    std::vector<uint8_t> db;
    while (position < total) {
        if (total - position < 16) fail("INVALID_CHUNK");
        std::array<uint8_t,16> chunk{}; read(chunk.data(),chunk.size());
        const auto start = position; position += 16; const auto length = be64(chunk.data() + 8);
        if (length > total - position) fail("INVALID_CHUNK_LENGTH");
        if (!memcmp(chunk.data(),"CHNKHead",8)) {
            if (head || start != 24 || length != 40) fail("UNSUPPORTED_HEADER");
            std::array<uint8_t,40> data{}; read(data.data(), data.size());
            if (be64(data.data()) != 256 || be64(data.data() + 16) != 16) fail("UNSUPPORTED_VERSION");
            sqlOffset = be64(data.data() + 8); head = true;
        } else if (!memcmp(chunk.data(),"CHNKExta",8)) {
            if (!head || length < 56) fail("INVALID_EXTERNAL_CHUNK");
            std::array<uint8_t,56> data{}; read(data.data(),data.size());
            if (be64(data.data()) != 40 || be64(data.data() + 48) != length - 56) fail("INVALID_EXTERNAL_LENGTH");
            skip(length - 56);
        } else if (!memcmp(chunk.data(),"CHNKSQLi",8)) {
            if (!head || !db.empty() || start != sqlOffset || length < 100) fail("INVALID_DATABASE_CHUNK");
            if (length > MetadataLimit) fail("LIMIT_METADATA");
            db.resize(static_cast<size_t>(length)); read(db.data(),db.size());
            if (memcmp(db.data(),"SQLite format 3\0",16)) fail("INVALID_DATABASE_SIGNATURE");
        } else if (!memcmp(chunk.data(),"CHNKFoot",8)) {
            if (length || db.empty() || position != total) fail("INVALID_FOOTER");
            footer = true;
        } else fail("UNSUPPORTED_CHUNK");
        position += length;
    }
    if (!footer || fgetc(stdin) != EOF) fail("INVALID_FILE_LENGTH");
    return db;
}
struct DbCloser { void operator()(sqlite3* db) const { sqlite3_close(db); } };
struct StatementCloser { void operator()(sqlite3_stmt* st) const { sqlite3_finalize(st); } };
using Statement = std::unique_ptr<sqlite3_stmt,StatementCloser>;
static void databaseFailure(sqlite3* db, const char* fallback) {
    switch (sqlite3_errcode(db)) {
    case SQLITE_NOMEM: fail("LIMIT_WORKING_MEMORY"); break;
    case SQLITE_INTERRUPT: fail("LIMIT_QUERY"); break;
    case SQLITE_TOOBIG: fail("LIMIT_ENCODED"); break;
    case SQLITE_CORRUPT: case SQLITE_NOTADB: fail("INVALID_DATABASE"); break;
    default: fail(fallback);
    }
}
static Statement query(sqlite3* db, const char* sql) {
    sqlite3_stmt* statement = nullptr;
    if (sqlite3_prepare_v2(db,sql,-1,&statement,nullptr) != SQLITE_OK) { sqlite3_finalize(statement); databaseFailure(db,"UNSUPPORTED_SCHEMA"); }
    return Statement(statement);
}
static void write32(uint32_t value) {
    std::array<uint8_t,4> data{uint8_t(value),uint8_t(value >> 8),uint8_t(value >> 16),uint8_t(value >> 24)};
    if (fwrite(data.data(),1,data.size(),stdout) != data.size()) fail("OUTPUT_FAILED");
}
static int progress(void* counter) { return ++*static_cast<int*>(counter) > 1000; }
int main() {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    _setmode(_fileno(stdin),_O_BINARY); _setmode(_fileno(stdout),_O_BINARY);
    try {
        auto data = database();
        if (sqlite3_config(SQLITE_CONFIG_MEMSTATUS,1) != SQLITE_OK) fail("DATABASE_CONFIGURATION_FAILED");
        sqlite3_hard_heap_limit64(512ll * 1024 * 1024);
        if (sqlite3_hard_heap_limit64(-1) != 512ll * 1024 * 1024) fail("DATABASE_CONFIGURATION_FAILED");
        sqlite3* rawDb = nullptr;
        if (sqlite3_open_v2(":memory:",&rawDb,SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE,nullptr) != SQLITE_OK) { sqlite3_close(rawDb); fail("DATABASE_OPEN_FAILED"); }
        std::unique_ptr<sqlite3,DbCloser> db(rawDb);
        if (sqlite3_deserialize(db.get(),"main",data.data(),static_cast<sqlite3_int64>(data.size()),static_cast<sqlite3_int64>(data.size()),SQLITE_DESERIALIZE_READONLY) != SQLITE_OK) fail("INVALID_DATABASE");
        sqlite3_db_config(db.get(),SQLITE_DBCONFIG_DEFENSIVE,1,nullptr);
        sqlite3_db_config(db.get(),SQLITE_DBCONFIG_TRUSTED_SCHEMA,0,nullptr);
        sqlite3_limit(db.get(),SQLITE_LIMIT_LENGTH,static_cast<int>(EncodedLimit));
        sqlite3_limit(db.get(),SQLITE_LIMIT_VDBE_OP,10000);
        int steps = 0; sqlite3_progress_handler(db.get(),1000,progress,&steps);
        auto canvases = query(db.get(),"SELECT CanvasRootFolder FROM Canvas LIMIT 2");
        if (sqlite3_step(canvases.get()) != SQLITE_ROW) databaseFailure(db.get(),"UNSUPPORTED_CANVAS_DIMENSIONS");
        if (sqlite3_step(canvases.get()) != SQLITE_DONE) databaseFailure(db.get(),"UNSUPPORTED_MULTIPLE_CANVASES");
        auto dimensions = query(db.get(),
            "SELECT substr(o.Attribute,1,118) FROM Canvas c JOIN Layer l ON l.MainId=c.CanvasRootFolder "
            "JOIN Mipmap m ON m.MainId=l.LayerRenderMipmap JOIN MipmapInfo i ON i.MainId=m.BaseMipmapInfo "
            "JOIN Offscreen o ON o.MainId=i.Offscreen LIMIT 1");
        if (sqlite3_step(dimensions.get()) != SQLITE_ROW) databaseFailure(db.get(),"UNSUPPORTED_CANVAS_DIMENSIONS");
        if (sqlite3_column_bytes(dimensions.get(),0) < 118) fail("UNSUPPORTED_CANVAS_DIMENSIONS");
        const auto attr = static_cast<const uint8_t*>(sqlite3_column_blob(dimensions.get(),0));
        if (!attr) fail("LIMIT_WORKING_MEMORY");
        const uint8_t parameter[18] = {0,'P',0,'a',0,'r',0,'a',0,'m',0,'e',0,'t',0,'e',0,'r'};
        if (be32(attr + 16) != 9 || memcmp(attr + 20,parameter,18)) fail("UNSUPPORTED_CANVAS_DIMENSIONS");
        const auto originalWidth = be32(attr + 38), originalHeight = be32(attr + 42);
        if (!originalWidth || !originalHeight || originalWidth > INT32_MAX || originalHeight > INT32_MAX) fail("UNSUPPORTED_CANVAS_DIMENSIONS");
        auto preview = query(db.get(),"SELECT ImageWidth,ImageHeight,ImageData FROM CanvasPreview LIMIT 2");
        if (sqlite3_step(preview.get()) != SQLITE_ROW) databaseFailure(db.get(),"MISSING_PREVIEW");
        const auto expectedWidth = sqlite3_column_int64(preview.get(),0), expectedHeight = sqlite3_column_int64(preview.get(),1);
        const auto size = sqlite3_column_bytes(preview.get(),2);
        if (size < 33) fail("MISSING_PREVIEW");
        const auto png = static_cast<const uint8_t*>(sqlite3_column_blob(preview.get(),2));
        if (!png) fail("LIMIT_WORKING_MEMORY");
        const uint8_t signature[8] = {137,80,78,71,13,10,26,10};
        if (memcmp(png,signature,8) || be32(png + 8) != 13 || memcmp(png + 12,"IHDR",4)) fail("UNSUPPORTED_PREVIEW_FORMAT");
        const auto width = be32(png + 16), height = be32(png + 20);
        if (!width || !height || uint64_t(width) * height > PixelLimit / 4) fail("LIMIT_PIXELS");
        if (expectedWidth != width || expectedHeight != height) fail("INVALID_PREVIEW_DIMENSIONS");
        if (static_cast<uint64_t>(size) > EncodedLimit) fail("LIMIT_ENCODED");
        if (fwrite("SVCP0001",1,8,stdout) != 8) fail("OUTPUT_FAILED");
        write32(width); write32(height); write32(originalWidth); write32(originalHeight); write32(static_cast<uint32_t>(size));
        if (fwrite(png,1,static_cast<size_t>(size),stdout) != static_cast<size_t>(size) || fflush(stdout)) fail("OUTPUT_FAILED");
        if (sqlite3_step(preview.get()) != SQLITE_DONE) databaseFailure(db.get(),"UNSUPPORTED_MULTIPLE_CANVASES");
        return 0;
    } catch (const std::bad_alloc&) { std::cerr << "LIMIT_WORKING_MEMORY\n"; return 1; }
      catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
