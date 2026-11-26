(() => {
    "use strict";

    function initTooltips() {
        if (typeof bootstrap === "undefined" || !bootstrap.Tooltip) return;
        document.querySelectorAll("[data-bs-toggle='tooltip']").forEach(el => {
            if (!bootstrap.Tooltip.getInstance(el)) {
                new bootstrap.Tooltip(el);
            }
        });
    }

    function initDrawers() {
        document.addEventListener("click", (e) => {
            const trigger = e.target.closest("[data-i3d-toggle]");
            if (!trigger) return;

            const targetId = trigger.getAttribute("data-i3d-toggle");
            const drawer = targetId
                ? document.getElementById(targetId)
                : trigger.closest(".i3d-component-card, .i3d-timeline-item")?.querySelector("[data-i3d-drawer]");

            if (!drawer) return;

            const isHidden = drawer.classList.contains("d-none");
            drawer.classList.toggle("d-none", !isHidden);
            trigger.setAttribute("aria-expanded", isHidden ? "true" : "false");

            const labelEl = trigger.querySelector("[data-i3d-toggle-label]");
            if (labelEl) {
                labelEl.textContent = isHidden
                    ? (trigger.getAttribute("data-label-open") || "收合詳情")
                    : (trigger.getAttribute("data-label-closed") || "展開詳情");
            }
        });
    }

    function syncSelectTilesFor(selectId) {
        const selectEl = document.getElementById(selectId);
        if (!selectEl) return;

        const tiles = document.querySelectorAll(`[data-select-target="${selectId}"]`);
        const currentValue = selectEl.value;

        tiles.forEach(tile => {
            const tileValue = tile.getAttribute("data-select-value") || "";
            const opt = Array.from(selectEl.options).find(o => o.value === tileValue);
            const isDisabled = opt ? opt.disabled : false;
            const isSelected = !isDisabled && tileValue === currentValue;

            tile.classList.toggle("is-selected", isSelected);
            tile.classList.toggle("is-disabled", isDisabled);
            tile.setAttribute("aria-pressed", isSelected ? "true" : "false");
            tile.setAttribute("aria-disabled", isDisabled ? "true" : "false");
        });
    }

    function syncCheckboxCards() {
        document.querySelectorAll(".i3d-checkbox-tile").forEach(card => {
            const cb = card.querySelector("input[type='checkbox']");
            if (!cb) return;
            card.classList.toggle("is-selected", cb.checked && !cb.disabled);
            card.classList.toggle("is-disabled", cb.disabled);
        });
    }

    function syncResultComponentCards() {
        document.querySelectorAll(".i3d-component-card").forEach(card => {
            const cb = card.querySelector(".component-checkbox");
            if (!cb) return;
            card.classList.toggle("is-checked", cb.checked);
        });
    }

    function syncAllSelectors() {
        const seenSelects = new Set();
        document.querySelectorAll("[data-select-target]").forEach(tile => {
            const selectId = tile.getAttribute("data-select-target");
            if (selectId && !seenSelects.has(selectId)) {
                seenSelects.add(selectId);
                syncSelectTilesFor(selectId);
            }
        });
        syncCheckboxCards();
        syncResultComponentCards();
    }

    function initSelectors() {
        document.addEventListener("click", (e) => {
            const selectTile = e.target.closest("[data-select-target]");
            if (selectTile) {
                if (selectTile.classList.contains("is-disabled")) return;
                const selectId = selectTile.getAttribute("data-select-target");
                const value = selectTile.getAttribute("data-select-value");
                const selectEl = selectId ? document.getElementById(selectId) : null;
                if (selectEl && value !== null) {
                    selectEl.value = value;
                    selectEl.dispatchEvent(new Event("change", { bubbles: true }));
                    syncSelectTilesFor(selectId);
                }
                return;
            }

            const cbTile = e.target.closest(".i3d-checkbox-tile");
            if (cbTile) {
                if (e.target.closest("a, button, input[type='checkbox'], label")) return;
                const cb = cbTile.querySelector("input[type='checkbox']");
                if (cb && !cb.disabled) {
                    cb.checked = !cb.checked;
                    cb.dispatchEvent(new Event("change", { bubbles: true }));
                    syncCheckboxCards();
                }
            }
        });

        document.addEventListener("keydown", (e) => {
            if (e.key !== "Enter" && e.key !== " ") return;
            const tile = e.target.closest("[data-select-target], .i3d-checkbox-tile");
            if (!tile || e.target.tagName === "INPUT") return;
            e.preventDefault();
            tile.click();
        });

        document.addEventListener("change", (e) => {
            if (e.target.matches("select") && e.target.id) {
                syncSelectTilesFor(e.target.id);
                syncCheckboxCards();
            } else if (e.target.matches("input[type='checkbox']")) {
                syncCheckboxCards();
                syncResultComponentCards();
            }
        });

        syncAllSelectors();
    }

    window.ECC3D = {
        syncAllSelectors,
        syncSelectTilesFor,
        syncCheckboxCards,
        syncResultComponentCards
    };

    document.addEventListener("DOMContentLoaded", () => {
        initTooltips();
        initDrawers();
        initSelectors();
    });
})();
