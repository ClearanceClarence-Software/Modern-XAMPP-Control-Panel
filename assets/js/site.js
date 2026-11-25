// The website's few behaviors. No dependencies; every page works without it.

const REPOSITORY = "ClearanceClarence-Software/Modern-XAMPP-Control-Panel";

// The menu button on narrow screens.
const menu = document.querySelector(".menu");
const nav = document.querySelector(".nav");
if (menu && nav) {
  menu.addEventListener("click", () => {
    const isOpen = nav.classList.toggle("open");
    menu.setAttribute("aria-expanded", String(isOpen));
  });
}

// A screenshot that is not in the repository yet shows as a labeled blank, not as a broken image.
function markMissing(image) {
  image.closest(".frame, .shot")?.classList.add("missing");
}
for (const image of document.querySelectorAll(".frame img, .shot img")) {
  if (image.complete && image.naturalWidth === 0) {
    markMissing(image);
  } else {
    image.addEventListener("error", () => markMissing(image));
  }
}

// Sections arrive as they scroll into view.
const rising = document.querySelectorAll(".rise");
if ("IntersectionObserver" in window && rising.length > 0) {
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        entry.target.classList.add("in");
        observer.unobserve(entry.target);
      }
    }
  }, { rootMargin: "0px 0px -8% 0px" });
  rising.forEach((element) => observer.observe(element));
} else {
  rising.forEach((element) => element.classList.add("in"));
}

// Copy buttons: <button data-copy="text">.
for (const button of document.querySelectorAll("[data-copy]")) {
  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(button.dataset.copy);
      const label = button.textContent;
      button.textContent = "Copied";
      setTimeout(() => { button.textContent = label; }, 1400);
    } catch {
      // The clipboard can be refused (no permission, not a secure page); the text is on the page to select.
    }
  });
}

// The newest release, asked from GitHub: fills in the version, the date, the zip's size and its direct link.
// Without an answer (offline, rate limit) the buttons keep pointing at the releases page, which always works.
async function showLatestRelease() {
  const targets = document.querySelectorAll("[data-latest]");
  if (targets.length === 0) {
    return;
  }
  try {
    const response = await fetch(`https://api.github.com/repos/${REPOSITORY}/releases/latest`, {
      headers: { Accept: "application/vnd.github+json" },
    });
    if (!response.ok) {
      return;
    }
    const release = await response.json();
    const zip = (release.assets ?? []).find((asset) => asset.name.endsWith("-win-x64.zip"));
    const version = String(release.tag_name ?? "").replace(/^v/, "");
    const date = release.published_at
      ? new Date(release.published_at).toLocaleDateString("en-US", { year: "numeric", month: "long", day: "numeric" })
      : "";
    const size = zip ? `${(zip.size / (1024 * 1024)).toFixed(0)} MB` : "";

    for (const target of targets) {
      const kind = target.dataset.latest;
      if (kind === "version" && version) {
        target.textContent = version;
      } else if (kind === "label" && version) {
        target.textContent = `v${version}`;
      } else if (kind === "date" && date) {
        target.textContent = date;
      } else if (kind === "size" && size) {
        target.textContent = size;
      } else if (kind === "file" && zip) {
        target.textContent = zip.name;
      } else if (kind === "link" && zip) {
        target.href = zip.browser_download_url;
      } else if (kind === "notes" && release.html_url) {
        target.href = release.html_url;
      }
    }
  } catch {
    // No network or no answer: the page keeps its defaults.
  }
}
showLatestRelease();
