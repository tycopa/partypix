/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Pages/**/*.{cshtml,razor,html}",
    "./Areas/**/*.{cshtml,razor,html}",
    "./Views/**/*.{cshtml,razor,html}",
    "./wwwroot/js/**/*.js",
  ],
  theme: {
    extend: {
      colors: {
        // Warm parchment background used site-wide.
        parchment: "#CABDAE",
        // Deep-bronze accent for buttons, links, primary text emphasis.
        bronze: {
          DEFAULT: "#6B4423",
          deep: "#4A2C14",
          light: "#8B5A2B",
        },
        // Reading ink tones that sit on top of the parchment.
        ink: {
          DEFAULT: "#2A1810",
          soft: "#6B5A48",
        },
      },
    },
  },
  plugins: [],
};
