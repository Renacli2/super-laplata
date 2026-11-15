// Funciones de navegador que usa la app (almacenamiento local y escáner de códigos).
window.superApp = (() => {
  let stream = null;
  let cancelled = false;

  function stopStream() {
    if (stream) {
      stream.getTracks().forEach(t => t.stop());
      stream = null;
    }
  }

  return {
    storageGet(key) {
      try { return localStorage.getItem(key); } catch { return null; }
    },

    storageSet(key, value) {
      try { localStorage.setItem(key, value); } catch { /* sin almacenamiento */ }
    },

    /**
     * Comparte el pedido con el menú del sistema (WhatsApp, Telegram, etc.).
     * Si el navegador no lo permite, copia el texto con el link. Devuelve "shared", "copied", "cancelled" o "failed".
     */
    async share(title, text, url) {
      if (navigator.share) {
        try {
          await navigator.share({ title, text, url });
          return 'shared';
        } catch (e) {
          if (e && e.name === 'AbortError') return 'cancelled';
        }
      }
      try {
        await navigator.clipboard.writeText(`${text}\n${url}`);
        return 'copied';
      } catch {
        return 'failed';
      }
    },

    /** El escáner usa BarcodeDetector (Chrome y Edge en Android). En el resto se carga el código a mano. */
    canScan() {
      return 'BarcodeDetector' in window && !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    },

    /**
     * Abre la cámara trasera en <video> y devuelve el primer EAN/UPC leído,
     * o null si se cancela. Lanza un error con un mensaje legible si no hay permiso o cámara.
     */
    async scan(video) {
      cancelled = false;
      const formats = ['ean_13', 'ean_8', 'upc_a', 'upc_e'];
      let detector;
      try {
        const supported = await BarcodeDetector.getSupportedFormats();
        detector = new BarcodeDetector({ formats: formats.filter(f => supported.includes(f)) });
      } catch {
        throw new Error('Este navegador no puede leer códigos de barras. Escribí el número que está debajo del código.');
      }

      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' } },
          audio: false
        });
      } catch (e) {
        throw new Error(e && e.name === 'NotAllowedError'
          ? 'La app no tiene permiso para usar la cámara. Habilitalo en la configuración del navegador.'
          : 'No se pudo abrir la cámara.');
      }

      video.srcObject = stream;
      await video.play();

      try {
        while (!cancelled) {
          const codes = await detector.detect(video);
          if (codes.length > 0) {
            if (navigator.vibrate) navigator.vibrate(60);
            return codes[0].rawValue;
          }
          await new Promise(r => setTimeout(r, 150));
        }
        return null;
      } finally {
        stopStream();
        video.srcObject = null;
      }
    },

    stopScan() {
      cancelled = true;
      stopStream();
    }
  };
})();
