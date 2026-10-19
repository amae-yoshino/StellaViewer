// Synthetic fixture writer used only by SmokeTests; never shipped with the viewer.
#include "sqlite3.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <vector>
#include <cstring>
#include <stdexcept>
#include <string>
static void sql(sqlite3* db,const char* text) { if(sqlite3_exec(db,text,nullptr,nullptr,nullptr)!=SQLITE_OK) throw std::runtime_error(sqlite3_errmsg(db)); }
static void be(std::ostream& out,uint64_t value,int length) { for(int i=length-1;i>=0;i--)out.put(static_cast<char>(value>>(i*8))); }
static void chunk(std::ostream& out,const char* name,uint64_t length) { out.write(name,8); be(out,length,8); }
int wmain(int argc,wchar_t** argv) {
    sqlite3* db=nullptr;
    try {
        if(argc!=6) throw std::runtime_error("Usage: fixture output.clip input.png originalWidth originalHeight kind");
        const uint32_t width=static_cast<uint32_t>(std::stoul(argv[3])),height=static_cast<uint32_t>(std::stoul(argv[4]));
        const std::wstring kind(argv[5]);
        std::ifstream input(std::filesystem::path(argv[2]),std::ios::binary);
        std::vector<uint8_t> png((std::istreambuf_iterator<char>(input)),{});
        if(png.size()<33)throw std::runtime_error("PNG fixture missing");
        auto u32=[](const uint8_t* p){return uint32_t(p[0])<<24|uint32_t(p[1])<<16|uint32_t(p[2])<<8|p[3];};
        auto pw=u32(png.data()+16),ph=u32(png.data()+20);
        if(sqlite3_open(":memory:",&db)!=SQLITE_OK)throw std::runtime_error("Cannot create fixture database");
        sql(db,"CREATE TABLE Canvas(CanvasRootFolder INTEGER,CanvasWidth REAL,CanvasHeight REAL,CanvasUnit INTEGER);"
            "INSERT INTO Canvas VALUES(2,42.5,30,1);"
            "CREATE TABLE Layer(MainId INTEGER,LayerRenderMipmap INTEGER); INSERT INTO Layer VALUES(2,3);"
            "CREATE TABLE Mipmap(MainId INTEGER,BaseMipmapInfo INTEGER); INSERT INTO Mipmap VALUES(3,4);"
            "CREATE TABLE MipmapInfo(MainId INTEGER,Offscreen INTEGER); INSERT INTO MipmapInfo VALUES(4,5);"
            "CREATE TABLE Offscreen(MainId INTEGER,Attribute BLOB);"
            "CREATE TABLE CanvasPreview(ImageWidth INTEGER,ImageHeight INTEGER,ImageData BLOB);");
        if(kind==L"query-limit")sql(db,"DROP TABLE Canvas; CREATE VIEW Canvas AS WITH RECURSIVE counter(x) AS "
            "(VALUES(0) UNION ALL SELECT x+1 FROM counter WHERE x<2000000) SELECT 2 AS CanvasRootFolder FROM counter WHERE x=2000000;");
        if(kind==L"multiple-canvas")sql(db,"INSERT INTO Canvas SELECT * FROM Canvas LIMIT 1;");
        std::vector<uint8_t> attr(118);
        attr[19]=9; const char* label="Parameter"; for(int i=0;i<9;i++)attr[21+i*2]=static_cast<uint8_t>(label[i]);
        for(int i=0;i<4;i++){attr[38+i]=uint8_t(width>>(24-8*i));attr[42+i]=uint8_t(height>>(24-8*i));}
        if(kind==L"dimensionless")attr[19]=0;
        sqlite3_stmt* st=nullptr;sqlite3_prepare_v2(db,"INSERT INTO Offscreen VALUES(5,?)",-1,&st,nullptr);
        sqlite3_bind_blob(st,1,attr.data(),static_cast<int>(attr.size()),SQLITE_TRANSIENT);sqlite3_step(st);sqlite3_finalize(st);
        if(kind!=L"missing") {
            sqlite3_prepare_v2(db,"INSERT INTO CanvasPreview VALUES(?,?,?)",-1,&st,nullptr);
            sqlite3_bind_int64(st,1,pw+(kind==L"mismatch"?1:0));sqlite3_bind_int64(st,2,ph);
            sqlite3_bind_blob(st,3,png.data(),static_cast<int>(png.size()),SQLITE_TRANSIENT);
            if(sqlite3_step(st)!=SQLITE_DONE)throw std::runtime_error("Cannot create preview fixture");sqlite3_finalize(st);
            if(kind==L"multiple")sql(db,"INSERT INTO CanvasPreview SELECT * FROM CanvasPreview LIMIT 1");
        }
        sqlite3_int64 size=0;auto bytes=sqlite3_serialize(db,"main",&size,0);
        if(!bytes)throw std::runtime_error("Cannot serialize fixture");
        std::ofstream output(std::filesystem::path(argv[1]),std::ios::binary|std::ios::trunc);
        output.write("CSFCHUNK",8);be(output,static_cast<uint64_t>(size)+112,8);be(output,24,8);
        chunk(output,"CHNKHead",40);be(output,256,8);be(output,80,8);be(output,16,8);be(output,0,8);be(output,0,8);
        chunk(output,"CHNKSQLi",static_cast<uint64_t>(size));output.write(reinterpret_cast<const char*>(bytes),size);
        chunk(output,"CHNKFoot",0);sqlite3_free(bytes);sqlite3_close(db);db=nullptr;
        if(!output)throw std::runtime_error("Cannot write fixture");
        return 0;
    }catch(const std::exception& error){if(db)sqlite3_close(db);std::cerr<<error.what()<<'\n';return 1;}
}
