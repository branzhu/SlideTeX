/**
 * mock-host.js – lightweight browser mock for SlideTeXHostObject.
 *
 * Guards:
 *  1. WebView2 COM bridge present → production, exit.
 *  2. window.slidetexHost already set → test harness injected first, exit.
 */
(function () {
  // Guard 1: real WebView2 COM bridge
  if (window.chrome && window.chrome.webview &&
      window.chrome.webview.hostObjects &&
      window.chrome.webview.hostObjects.sync &&
      window.chrome.webview.hostObjects.sync.slidetexHost) {
    return;
  }

  // Guard 2: test harness (e.g. test-main-flow.mjs evaluateOnNewDocument)
  if (window.slidetexHost) {
    return;
  }

  var TAG = '[mock-host]';

  window.slidetexHost = {
    notifyRenderSuccess: function (json) {
      console.log(TAG, 'notifyRenderSuccess', json);
    },
    notifyRenderError: function (json) {
      console.log(TAG, 'notifyRenderError', json);
    },
    requestInsert: function (json) {
      console.log(TAG, 'requestInsert', json);
    },
    requestUpdate: function (json) {
      console.log(TAG, 'requestUpdate', json);
    },
    requestOpenPane: function () {
      console.log(TAG, 'requestOpenPane');
    },
    requestEditSelected: function (json) {
      console.log(TAG, 'requestEditSelected', json);
    },
    requestRenumber: function (json) {
      console.log(TAG, 'requestRenumber', json);
    },
    requestFormulaOcr: function (json) {
      console.log(TAG, 'requestFormulaOcr', json);
      // Simulate async OCR callback
      setTimeout(function () {
        if (window.slideTex && typeof window.slideTex.onFormulaOcrSuccess === 'function') {
          window.slideTex.onFormulaOcrSuccess({ latex: '\\frac{a}{b}' });
        }
      }, 500);
    }
  };

  console.log(TAG, 'Mock host active (browser mode)');
})();
