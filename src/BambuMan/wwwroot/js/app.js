window.bambuman = {
    // Focus a field and select its content, so a freshly read spool weight can be typed over straight away.
    focusSelect: function (element) {
        if (!element) return;

        element.focus();
        if (typeof element.select === "function") element.select();
    },

    blur: function () {
        if (document.activeElement && typeof document.activeElement.blur === "function") document.activeElement.blur();
    }
};
