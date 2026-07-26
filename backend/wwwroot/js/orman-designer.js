(function () {
    let dotNetRef = null;
    let activeMarker = null;
    let activeMap = null;
    let activeShelfId = null;

    function updateMarker(clientX, clientY) {
        if (!activeMarker || !activeMap || !dotNetRef) {
            return;
        }

        const rect = activeMap.getBoundingClientRect();
        const baseHeight = Number(activeMap.dataset.baseHeight || "225");
        const x = Math.max(0, Math.min(300, Math.round((clientX - rect.left) / rect.width * 300)));
        const y = Math.max(0, Math.min(baseHeight, Math.round((clientY - rect.top) / rect.height * baseHeight)));

        activeMarker.style.left = `calc(var(--orman-scale, 1) * ${x}px)`;
        activeMarker.style.top = `calc(var(--orman-scale, 1) * ${y}px)`;
        dotNetRef.invokeMethodAsync("UpdateDraftShelfPosition", activeShelfId, x, y);
    }

    document.addEventListener("pointerdown", function (event) {
        const marker = event.target.closest(".ormani-designer-marker");
        if (!marker) {
            return;
        }

        const map = marker.closest("[data-orman-designer]");
        if (!map) {
            return;
        }

        activeMarker = marker;
        activeMap = map;
        activeShelfId = Number(marker.dataset.shelfId);
        marker.setPointerCapture?.(event.pointerId);
        event.preventDefault();
    });

    document.addEventListener("pointermove", function (event) {
        if (!activeMarker) {
            return;
        }

        updateMarker(event.clientX, event.clientY);
        event.preventDefault();
    }, { passive: false });

    document.addEventListener("pointerup", function (event) {
        if (!activeMarker) {
            return;
        }

        updateMarker(event.clientX, event.clientY);
        activeMarker = null;
        activeMap = null;
        activeShelfId = null;
    });

    document.addEventListener("pointercancel", function () {
        activeMarker = null;
        activeMap = null;
        activeShelfId = null;
    });

    window.addEventListener("resize", function () {
        window.ormanDesigner?.setAllMapRatios();
    });

    window.ormanDesigner = {
        register(ref) {
            dotNetRef = ref;
        },
        setMapRatio(image) {
            if (!image?.naturalWidth || !image?.naturalHeight) {
                return;
            }

            const map = image.closest(".cabinet-ratio-map");
            if (!map) {
                return;
            }

            map.style.setProperty("--orman-ratio", `${image.naturalWidth} / ${image.naturalHeight}`);
            map.dataset.baseHeight = Math.round(300 * image.naturalHeight / image.naturalWidth).toString();
            const baseHeight = Number(map.dataset.baseHeight || "225");
            const displayWidth = Math.min(500, 400 * 300 / Math.max(1, baseHeight));
            map.style.setProperty("--orman-display-width", `${displayWidth}px`);
            map.style.setProperty("--orman-scale", (map.getBoundingClientRect().width / 300).toString());
        },
        setAllMapRatios() {
            document.querySelectorAll(".cabinet-ratio-map .cabinet-image").forEach(image => this.setMapRatio(image));
        },
        unregister() {
            dotNetRef = null;
        }
    };
})();
