// Config for `npm run gen:icons` (@vite-pwa/assets-generator): renders every icon size the manifest, Android and
// iOS need from public/logo.svg, writing the PNGs next to it in public/.
import { defineConfig, minimal2023Preset } from '@vite-pwa/assets-generator/config'

const APP_BLUE = '#1565c0'

export default defineConfig({
  preset: {
    ...minimal2023Preset,
    // Maskable: Android crops icons to a circle, squircle, etc. The important part must sit inside the "safe zone",
    // a centred circle 80% of the icon's width across. The logo is shrunk by `padding` and the gap filled with
    // the same blue as its background. The preset's default 0.3 shrinks it to about half; 0.1 keeps the outer ring
    // at ~31% radius, still inside the 40% safe zone.
    maskable: {
      ...minimal2023Preset.maskable,
      padding: 0.1,
      resizeOptions: { background: APP_BLUE },
    },
    // iOS doesn't support transparent home-screen icons (it fills transparency with black), so give the
    // rounded corners a solid blue background. iOS only rounds the corners slightly, so little padding is needed.
    apple: {
      ...minimal2023Preset.apple,
      padding: 0.05,
      resizeOptions: { background: APP_BLUE },
    },
  },
  images: ['public/logo.svg'],
})
