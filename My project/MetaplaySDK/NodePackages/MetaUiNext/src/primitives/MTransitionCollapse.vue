<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- Adapted from sample code by https://stackoverflow.com/users/4280547/kostyfisik -->

<template lang="pug">
Transition(
  :css="false"
  @enter="enterTransition"
  @leave="leaveTransition"
  )
  slot
</template>

<script setup lang="ts">
const closed = '0px'

/**
 * Subset of the HTMLElementStyles that we are interested in.
 */
interface HTMLStyle {
  height: string
  width: string
  position: string
  visibility: string
  overflow: string
  paddingTop: string
  paddingBottom: string
  borderTopWidth: string
  borderBottomWidth: string
  marginTop: string
  marginBottom: string
}

/**
 * Get the current styles of an element.
 */
function getElementStyle (element: HTMLElement) {
  return {
    height: element.style.height,
    width: element.style.width,
    position: element.style.position,
    visibility: element.style.visibility,
    overflow: element.style.overflow,
    paddingTop: element.style.paddingTop,
    paddingBottom: element.style.paddingBottom,
    borderTopWidth: element.style.borderTopWidth,
    borderBottomWidth: element.style.borderBottomWidth,
    marginTop: element.style.marginTop,
    marginBottom: element.style.marginBottom,
  }
}

/**
 * Monkey with the original element to prepare it for animating in.
 */
function prepareElement (element: HTMLElement, initialStyle: HTMLStyle) {
  const { width } = getComputedStyle(element)
  element.style.width = width
  element.style.position = 'absolute'
  element.style.visibility = 'hidden'
  element.style.height = ''
  const { height } = getComputedStyle(element)
  element.style.width = initialStyle.width
  element.style.position = initialStyle.position
  element.style.visibility = initialStyle.visibility
  element.style.height = closed
  element.style.overflow = 'hidden'
  return initialStyle.height && initialStyle.height !== closed
    ? initialStyle.height
    : height
}

/**
 * Animate the transition of an element using the Web Animation API.
 */
function animateTransition (
  element: HTMLElement,
  initialStyle: HTMLStyle,
  done: () => void,
  keyframes: Keyframe[] | PropertyIndexedKeyframes | null,
  options?: number | KeyframeAnimationOptions
) {
  const animation = element.animate(keyframes, options)
  // Set height to 'auto' to restore it after animation
  element.style.height = initialStyle.height
  animation.onfinish = () => {
    element.style.overflow = initialStyle.overflow
    done()
  }
}

/**
 * Get the animation keyframes.
 */
function getEnterKeyframes (height: string, initialStyle: HTMLStyle) {
  return [
    {
      height: closed,
      opacity: 0,
      paddingTop: closed,
      paddingBottom: closed,
      borderTopWidth: closed,
      borderBottomWidth: closed,
      marginTop: closed,
      marginBottom: closed,
    },
    {
      height,
      opacity: 1,
      paddingTop: initialStyle.paddingTop,
      paddingBottom: initialStyle.paddingBottom,
      borderTopWidth: initialStyle.borderTopWidth,
      borderBottomWidth: initialStyle.borderBottomWidth,
      marginTop: initialStyle.marginTop,
      marginBottom: initialStyle.marginBottom,
    },
  ]
}

function enterTransition (element: Element, done: () => void) {
  const HTMLElement = element as HTMLElement
  const initialStyle = getElementStyle(HTMLElement)
  const height = prepareElement(HTMLElement, initialStyle)
  const keyframes = getEnterKeyframes(height, initialStyle)
  const options = { duration: 300, easing: 'ease-in-out' }
  animateTransition(HTMLElement, initialStyle, done, keyframes, options)
}

function leaveTransition (element: Element, done: () => void) {
  const HTMLElement = element as HTMLElement
  const initialStyle = getElementStyle(HTMLElement)
  const { height } = getComputedStyle(HTMLElement)
  HTMLElement.style.height = height
  HTMLElement.style.overflow = 'hidden'
  const keyframes = getEnterKeyframes(height, initialStyle).reverse()
  const options = { duration: 300, easing: 'ease-in-out' }
  animateTransition(HTMLElement, initialStyle, done, keyframes, options)
}
</script>
