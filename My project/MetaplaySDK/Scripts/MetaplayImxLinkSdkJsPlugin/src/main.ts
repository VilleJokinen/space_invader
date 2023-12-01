import { Link } from '@imtbl/imx-sdk'

const IMX_SETTINGS_KEY = "metaplay-imx-settings";

interface ImxWalletAddress {
  ethAddress: string
  imxAddress: string
}

let _current_api_url: string | null = null
let _current_link: Link | null = null

function EnsureInitialized(apiUrl: string | null) {
  if (_current_api_url === apiUrl) {
    return
  }
  // canonize "" -> null
  if (apiUrl === "") {
    apiUrl = null
  }
  _current_api_url = apiUrl
  _current_link = apiUrl === null ? null : new Link(apiUrl)
}

function CheckInitialized() {
  if (_current_api_url === null) {
    throw new Error("IMX Link SDK is not initialized. Either IMX is not configured in server or connection has not been established yet.")
  }
}

/**
 * Returns the Environment specific config object. Object may contain arbitrary keys.
 */
function GetEnvConfig(): any {
  CheckInitialized()
  try {
    if (!window.localStorage) {
      return {}
    }
  } catch {
    // access to local storage is denied
    return {}
  }

  const settingsStr = window.localStorage.getItem(IMX_SETTINGS_KEY)
  if (settingsStr == null) {
    return {}
  }
  const settings = JSON.parse(settingsStr)
  if (settings && settings.envs && settings.envs[_current_api_url!]) {
    return settings.envs[_current_api_url!]
  }
  return {}
}

/**
 * Sets the Environment specific config object. Object may contain arbitrary keys.
 */
function SetEnvConfig(envSettings: any): void {
  CheckInitialized()
  try {
    if (!window.localStorage) {
      return
    }
  } catch {
    // access to local storage is denied
    return
  }

  let allSettings
  const settingsStr = window.localStorage.getItem(IMX_SETTINGS_KEY)
  if (settingsStr == null) {
    allSettings = {}
  } else {
    allSettings = JSON.parse(settingsStr)
  }

  allSettings = allSettings ?? {}
  allSettings.envs = allSettings.env ?? {}
  allSettings.envs[_current_api_url!] = envSettings

  window.localStorage.setItem(IMX_SETTINGS_KEY, JSON.stringify(allSettings))
}

/**
 * Retrieves currently active IMX wallet. If no wallet has been previously attached or if forceResetup is set,
 * perform the IMX first-time-setup.
 */
async function GetWalletAddressAsync(forceResetup: boolean = false): Promise<ImxWalletAddress> {
  CheckInitialized()
  let settings = GetEnvConfig()
  if (forceResetup || !settings.walletEthAddress || !settings.walletImxAddress) {
    const { address, starkPublicKey } = await _current_link!.setup({})

    settings.walletEthAddress = address
    settings.walletImxAddress = starkPublicKey
    SetEnvConfig(settings)
  }

  return {
    ethAddress: settings.walletEthAddress,
    imxAddress: settings.walletImxAddress,
  }
}

async function TrySignAsync(message: string, description: string): Promise<string | null> {
  CheckInitialized()
  const result = await _current_link!.sign({message: message, description: description})
  if (result?.result) {
    return result?.result
  }
  return null
}

export { EnsureInitialized, GetWalletAddressAsync, TrySignAsync }
