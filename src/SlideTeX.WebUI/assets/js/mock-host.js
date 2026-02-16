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

  // Wrap .app in a resizable container so developers can drag to simulate
  // different task-pane widths. PowerPoint sets _taskPane.Width = 700 (physical
  // pixels). The browser maps that to 700/devicePixelRatio CSS pixels, so the
  // default here is 700 CSS px divided by the current DPR.
  var app = document.querySelector('.app');
  if (app) {
    var wrapper = document.createElement('div');
    wrapper.id = 'mock-pane';
    app.parentNode.insertBefore(wrapper, app);
    wrapper.appendChild(app);

    var handle = document.createElement('div');
    handle.id = 'mock-pane-handle';
    wrapper.appendChild(handle);

    var widthLabel = document.createElement('div');
    widthLabel.id = 'mock-pane-width';
    wrapper.appendChild(widthLabel);

    var style = document.createElement('style');
    style.textContent = [
      '#mock-pane {',
      '  width: ' + Math.round(700 / window.devicePixelRatio) + 'px;',
      '  max-width: 100vw;',
      '  margin: 0 auto;',
      '  overflow: hidden;',
      '  border: 2px dashed #b0b8c4;',
      '  border-radius: 6px;',
      '  position: relative;',
      '}',
      '#mock-pane-handle {',
      '  position: absolute;',
      '  top: 0; right: -6px;',
      '  width: 12px; height: 100%;',
      '  cursor: ew-resize;',
      '  z-index: 10;',
      '}',
      '#mock-pane-handle::after {',
      '  content: "";',
      '  position: absolute;',
      '  top: 50%; right: 4px;',
      '  transform: translateY(-50%);',
      '  width: 4px; height: 32px;',
      '  border-radius: 2px;',
      '  background: #b0b8c4;',
      '}',
      '#mock-pane-width {',
      '  position: absolute;',
      '  bottom: 4px; right: 8px;',
      '  font-size: 11px;',
      '  color: #9ca3af;',
      '  pointer-events: none;',
      '}'
    ].join('\n');
    document.head.appendChild(style);

    function updateLabel() {
      widthLabel.textContent = wrapper.offsetWidth + 'px';
    }
    updateLabel();

    handle.addEventListener('mousedown', function (e) {
      e.preventDefault();
      var startX = e.clientX;
      var startW = wrapper.offsetWidth;
      function onMove(ev) {
        var w = Math.max(120, startW + (ev.clientX - startX));
        wrapper.style.width = w + 'px';
        updateLabel();
      }
      function onUp() {
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', onUp);
      }
      document.addEventListener('mousemove', onMove);
      document.addEventListener('mouseup', onUp);
    });
  }
})();
