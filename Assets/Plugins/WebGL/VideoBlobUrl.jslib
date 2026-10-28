mergeInto(LibraryManager.library, {
  // C#から渡された動画のバイト列からBlob URLを作り、その文字列を返す
  CreateVideoBlobUrl: function (dataPtr, length, mimeTypePtr) {
    var bytes = HEAPU8.slice(dataPtr, dataPtr + length);
    var blob = new Blob([bytes], { type: UTF8ToString(mimeTypePtr) });
    var url = URL.createObjectURL(blob);

    var size = lengthBytesUTF8(url) + 1;
    var buffer = _malloc(size);
    stringToUTF8(url, buffer, size);
    return buffer;
  }
});
