mergeInto(LibraryManager.library, {
  AIClientStreamBegin: function (goPtr, urlPtr, jsonPtr) {
    var s = (Module.AIClientStreamState = { go: UTF8ToString(goPtr), len: 0, xhr: null });
    var xhr = new XMLHttpRequest();
    xhr.open("POST", UTF8ToString(urlPtr), true);
    xhr.setRequestHeader("Content-Type", "application/json");
    xhr.onreadystatechange = function () {
      if (xhr.readyState === 3 || xhr.readyState === 4) {
        var text = xhr.responseText.substring(s.len);
        s.len = xhr.responseText.length;
        if (text.length) SendMessage(s.go, "OnStreamChunk", text);
      }
      if (xhr.readyState === 4) {
        var ok = xhr.status >= 200 && xhr.status < 300;
        SendMessage(s.go, ok ? "OnStreamComplete" : "OnStreamError",
                    ok ? "" : xhr.status + " " + xhr.responseText);
      }
    };
    xhr.onerror = function () { SendMessage(s.go, "OnStreamError", "网络错误"); };
    s.xhr = xhr;
    xhr.send(UTF8ToString(jsonPtr));
  },
  AIClientStreamAbort: function () {
    var s = Module.AIClientStreamState;
    if (s && s.xhr) { s.xhr.abort(); s.xhr = null; }
  }
});