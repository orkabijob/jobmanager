/* glass-glint.js — Specular highlight pointer tracking for Liquid Glass
   - Updates --lg-glint-x on hero surfaces via pointermove
   - Throttled; disabled under prefers-reduced-motion and battery-saver API
   - Vanilla JS, no deps, ~1KB
   - RTL-safe: uses getBoundingClientRect (physical coords, direction-neutral) */

(function () {
  'use strict';

  /* Honor reduced-motion — mirrors Apple's battery-saver gate */
  var motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
  if (motionQuery.matches) return;

  /* Battery API gate (Chrome/Android) */
  if (navigator.getBattery) {
    navigator.getBattery().then(function (bat) {
      if (bat.level < 0.20 && !bat.charging) return;
      init();
      bat.addEventListener('levelchange', function () {
        if (bat.level < 0.20 && !bat.charging) teardown();
      });
    }).catch(init); /* no battery API = desktop; always init */
  } else {
    init();
  }

  var raf = null;
  var targets = [];

  function init() {
    /* Find all .glass elements that have explicit glint tracking enabled
       (auth card, admin metric hero, any element with data-glint) */
    targets = Array.prototype.slice.call(
      document.querySelectorAll('.glass--lifted, .glass--enter, [data-glint]')
    );
    if (targets.length === 0) return;
    document.addEventListener('pointermove', onPointerMove, { passive: true });
  }

  function teardown() {
    document.removeEventListener('pointermove', onPointerMove);
    if (raf) { cancelAnimationFrame(raf); raf = null; }
  }

  var lastX = 0, lastY = 0;

  function onPointerMove(e) {
    lastX = e.clientX;
    lastY = e.clientY;
    if (!raf) {
      raf = requestAnimationFrame(updateGlint);
    }
  }

  function updateGlint() {
    raf = null;
    targets.forEach(function (el) {
      var rect = el.getBoundingClientRect();
      /* Skip if off-screen */
      if (rect.width === 0) return;
      /* Percentage of pointer position within the element's physical box
         Clamped: 10%–90% so the glint never fully leaves the surface */
      var xPct = (lastX - rect.left) / rect.width;
      xPct = Math.max(0.10, Math.min(0.90, xPct));
      /* Intensity: closer to center = slightly brighter */
      var intensity = 0.45 + 0.22 * (1 - Math.abs(xPct - 0.5) * 2);
      el.style.setProperty('--lg-glint-x', (xPct * 100).toFixed(1) + '%');
      el.style.setProperty('--lg-glint-intensity', intensity.toFixed(2));
    });
  }

  /* Re-query targets on DOM changes (single-page navigation) */
  if (typeof MutationObserver !== 'undefined') {
    var mo = new MutationObserver(function () {
      targets = Array.prototype.slice.call(
        document.querySelectorAll('.glass--lifted, .glass--enter, [data-glint]')
      );
    });
    mo.observe(document.body, { childList: true, subtree: true });
  }

})();
