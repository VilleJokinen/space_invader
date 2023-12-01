// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Globally useful Bootstrap-Vue components
// Note: this allows the compiler to discard unused components for a smaller and faster builds

import {
  FormPlugin,
  FormGroupPlugin,
  FormInputPlugin,
  FormTextareaPlugin,
  FormRadioPlugin,
  FormSelectPlugin,
  ButtonPlugin,
  LayoutPlugin,
  AlertPlugin,
  CardPlugin,
  SpinnerPlugin,
  ListGroupPlugin,
  TablePlugin,
  CollapsePlugin,
  ModalPlugin,
  FormCheckboxPlugin,
  InputGroupPlugin,
  PopoverPlugin,
  LinkPlugin,
  ImagePlugin,
  SkeletonPlugin,
  AvatarPlugin,
  DropdownPlugin
} from 'bootstrap-vue'

// Font awesome icons
import { library } from '@fortawesome/fontawesome-svg-core'
import {
  faTachometerAlt,
  faUsers,
  faUser,
  faSignOutAlt,
  faSignInAlt,
  faPaperPlane,
  faBroadcastTower,
  faCommentAlt,
  faCog,
  faCogs,
  faCode,
  faCircle,
  faClipboardList,
  faAngleDoubleLeft,
  faHamburger,
  faLock,
  faPlusSquare,
  faCalendarAlt,
  faTimes,
  faClone,
  faFileDownload,
  faTrashAlt,
  faCloud,
  faExternalLinkAlt,
  faBug,
  faTable,
  faUserTag,
  faTags,
  faChess,
  faChessRook,
  faSearch,
  faAngleRight,
  faSyncAlt,
  faSortAmountDown,
  faSortAmountUp,
  faInbox,
  faKey,
  faDatabase,
  faAmbulance,
  faMoneyCheckAlt,
  faClock,
  faBusinessTime,
  faQuestionCircle,
  faExclamationTriangle,
  faPaperclip,
  faSlidersH,
  faList,
  faPeopleArrows,
  faExchangeAlt,
  faLanguage,
  faIdCard,
  faTabletAlt,
  faChartLine,
  faFlask,
  faBinoculars,
  faChartBar,
  faCoins,
  faArrowRight,
  faArrowLeft,
  faArrowDown,
  faTag,
  faWrench,
  faBackward,
  faFastBackward,
  faForward,
  faFastForward,
  faUserAstronaut,
  faInfoCircle,
  faLockOpen,
  faChessKnight,
  faCodeBranch,
  faIndustry,
  faRightLeft,
  faAngleDown,
  faCopy,
  faCheck,
  faXmark,
  faShieldHalved,
  faCubes,
  faCube,
  faTrophy,
  faDesktop,
  faBellSlash,
  faRobot,
  faCircleExclamation,
} from '@fortawesome/free-solid-svg-icons'
import { faCircle as farCircle, faWindowClose, faCaretSquareRight } from '@fortawesome/free-regular-svg-icons'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'

// Metaplay components and libraries
import '@metaplay/bootstrap'
import MetaUiPlugin from '@metaplay/meta-ui'

import { router } from './index'
import type { App } from 'vue'
import { createPinia } from 'pinia'

// ----- Actual init code starts here -----
export function CorePlugin (app: App) {
  // Initialize Pinia
  const pinia = createPinia()
  app.use(pinia)

  // Core router
  app.use(router)

  // Global UI components
  app.use(MetaUiPlugin)

  // Bootstrap-Vue
  app.use(FormPlugin)
  app.use(FormGroupPlugin)
  app.use(FormInputPlugin)
  app.use(FormTextareaPlugin)
  app.use(FormRadioPlugin)
  app.use(FormSelectPlugin)
  app.use(ButtonPlugin)
  app.use(LayoutPlugin)
  app.use(AlertPlugin)
  app.use(CardPlugin)
  app.use(SpinnerPlugin)
  app.use(ListGroupPlugin)
  app.use(TablePlugin)
  app.use(CollapsePlugin)
  app.use(ModalPlugin)
  app.use(FormCheckboxPlugin)
  app.use(InputGroupPlugin)
  app.use(PopoverPlugin)
  app.use(LinkPlugin)
  app.use(ImagePlugin)
  app.use(SkeletonPlugin)
  app.use(AvatarPlugin)
  app.use(DropdownPlugin)

  // Font Awesome
  library.add(
    faTachometerAlt,
    faUsers,
    faUser,
    faSignOutAlt,
    faSignInAlt,
    faPaperPlane,
    faBroadcastTower,
    faCommentAlt,
    faCog,
    faCogs,
    faCode,
    faCircle,
    farCircle,
    faClipboardList,
    faAngleDoubleLeft,
    faHamburger,
    faLock,
    faPlusSquare,
    faCalendarAlt,
    faTimes,
    faClone,
    faFileDownload,
    faTrashAlt,
    faWindowClose,
    faCloud,
    faExternalLinkAlt,
    faBug,
    faTable,
    faUserTag,
    faTags,
    faChess,
    faChessRook,
    faSearch,
    faAngleRight,
    faSyncAlt,
    faSortAmountDown,
    faSortAmountUp,
    faInbox,
    faKey,
    faDatabase,
    faAmbulance,
    faMoneyCheckAlt,
    faClock,
    faBusinessTime,
    faQuestionCircle,
    faExclamationTriangle,
    faPaperclip,
    faSlidersH,
    faList,
    faPeopleArrows,
    faExchangeAlt,
    faLanguage,
    faIdCard,
    faTabletAlt,
    faChartLine,
    faFlask,
    faBinoculars,
    faChartBar,
    faCoins,
    faArrowRight,
    faArrowLeft,
    faArrowDown,
    faTag,
    faWrench,
    faBackward,
    faFastBackward,
    faForward,
    faFastForward,
    faUserAstronaut,
    faInfoCircle,
    faLockOpen,
    faChessKnight,
    faCodeBranch,
    faIndustry,
    faCaretSquareRight,
    faRightLeft,
    faAngleDown,
    faCopy,
    faCheck,
    faXmark,
    faShieldHalved,
    faCubes,
    faCube,
    faTrophy,
    faDesktop,
    faBellSlash,
    faRobot,
    faCircleExclamation,
  )
  app.component('FaIcon', FontAwesomeIcon)
}
