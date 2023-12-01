import { computed, ref, type App } from 'vue'
import { makeIntoUniqueKey } from '../utils/generalUtils'

/**
 * A notification to be shown to the user.
 */
export interface Notification {
  id: number
  title: string
  message: string
  variant: 'danger' | 'success' | 'warning'
  lifetime: number
  isHovered?: boolean
}

const config = {
  maxNotifications: 10,
  maxTitleLength: 60,
  maxMessageLength: 280,
}

/**
 * A list of notifications. Not all notifications will be shown at once.
 */
const allNotifications = ref<Notification[]>([])

/**
 * Oldest notifications. Maximum of four at a time.
 */
const notificationsToShow = computed(() => allNotifications.value.slice(0, 4))

function addNotification (notification: Notification): number | undefined {
  // Limit the notifications que to 10 because anything more is useless.
  if (allNotifications.value.length > config.maxNotifications) {
    console.warn('Too many notifications. Ignoring:', notification.message)
    return
  }

  // Truncate the title if it's too long.
  if (notification.title.length > config.maxTitleLength) {
    notification.title = notification.title.slice(0, config.maxTitleLength) + '...'
  }

  // Truncate the message if it's too long.
  if (notification.message.length > config.maxMessageLength) {
    notification.message = notification.message.slice(0, config.maxMessageLength) + '...'
  }
  // Ignore duplicate notifications.
  if (allNotifications.value.find((existingNotification) => existingNotification.message === notification.message && existingNotification.title === notification.title)) {
    return
  }

  allNotifications.value.push(notification)

  return notification.id
}

let nextId = 0
function getUniqueId () {
  return nextId++
}

function showSuccessNotification (message: string, title: string = 'Done'): number | undefined {
  return addNotification({
    id: getUniqueId(),
    title,
    message,
    variant: 'success',
    lifetime: 3000,
  })
}

function showWarningNotification (message: string, title: string = 'Warning'): number | undefined {
  return addNotification({
    id: getUniqueId(),
    title,
    message,
    variant: 'warning',
    lifetime: 4000,
  })
}

function showErrorNotification (message: string, title: string = 'Error'): number | undefined {
  return addNotification({
    id: getUniqueId(),
    title,
    message,
    variant: 'danger',
    lifetime: 5000,
  })
}

function removeNotification (id: number) {
  allNotifications.value = allNotifications.value.filter((notification) => notification.id !== id)
}

function updateNotification (id: number | undefined, notification: Partial<Notification>) {
  // Ignore if no ID is provided. This can happen if the notification was never created.
  if (!id) return

  // If there's no notification with the provided ID, make a new one.
  const existingNotification = allNotifications.value.find((notification) => notification.id === id)
  if (!existingNotification) {
    const newNotification = {
      id,
      title: 'Done',
      message: '',
      variant: 'success',
      lifetime: 3000,
    } satisfies Notification
    Object.assign(newNotification, notification)
    allNotifications.value.push(newNotification)
  } else {
    Object.assign(existingNotification, notification)
  }
}

function tickNotificationLifetimes (amount: number) {
  for (const notification of notificationsToShow.value) {
    if (!notification.isHovered) notification.lifetime -= amount
  }

  // Remove any notifications that have expired.
  allNotifications.value = allNotifications.value.filter((notification) => notification.lifetime > 0)
}

function updateHoverStatus (id: number, isHovered: boolean) {
  const notification = allNotifications.value.find((notification) => notification.id === id)
  if (!notification) return
  notification.isHovered = isHovered
}

export function useNotifications () {
  return {
    /**
     * Add a notification to show to the user. Duplicate notifications will be ignored. Maximum message length is 280 characters before the message is truncated.
     * @param message Body message for the notification.
     * @param title Optional: Title for the notification. Defaults to 'Done'.
     * @returns ID of the notification.
     */
    showSuccessNotification,
    /**
     * Add a notification to show to the user. Duplicate notifications will be ignored. Maximum message length is 280 characters before the message is truncated.
     * @param message Body message for the notification.
     * @param title Optional: Title for the notification. Defaults to 'Warning'.
     * @returns ID of the notification.
     */
    showWarningNotification,
    /**
     * Add a notification to show to the user. Duplicate notifications will be ignored. Maximum message length is 280 characters before the message is truncated.
     * @param message Body message for the notification.
     * @param title Optional: Title for the notification. Defaults to 'Error'.
     * @returns ID of the notification.
     */
    showErrorNotification,
    /**
     * Oldest notifications. Maximum of four at a time.
     * */
    notificationsToShow,
    /**
     * Remove a notification from the list of notifications to show.
     * @param id ID of the notification to remove.
     */
    removeNotification,
    /**
     * Update an existing notification.
     * @param id ID of the notification to update.
     */
    updateNotification,
    /**
     * Tick down the lifetime of all displayed notifications.
     * @param amount Amount to tick down by.
     */
    tickNotificationLifetimes,
    /**
     * Update the hover status of a notification. Hovered notifications will not tick down their lifetimes.
     * @param id ID of the notification to update.
     * @param isHovered Whether the notification is hovered or not.
     **/
    updateHoverStatus,
  }
}

/**
 * An optional Vue plugin to hook into and show the native errors and warnings as notifications.
 * @param app the Vue app to install the plugin into.
 */
export function useNotificationsVuePlugin (app: App) {
  // Register global error handlers
  app.config.errorHandler = function (err: any, vm, info) {
    // eslint-disable-next-line
    console.error(err)
    if (err) showErrorNotification(err.message, 'Frontend Error')
  }

  // Vue warnings only happen in development builds, so this code will never fire in production.
  app.config.warnHandler = function (msg, instance, trace) {
    // eslint-disable-next-line
    console.warn(`${msg}\n${trace}`)
    showWarningNotification(msg, 'Frontend Warning')
  }
}
