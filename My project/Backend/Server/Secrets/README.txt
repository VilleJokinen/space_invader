Caveat: despite being named Secrets, this directory isn't treated any more
securely than any of the other directories, such as for example ../Config. In
particular, it's in the repository like anything else, and it gets baked in the
server image the same way as ../Config.

Currently, the only purpose of this directory is to have separate places for
basic configuration (../Config), and for credentials and such (this directory);
this separation exists only for clarity.

Generally speaking, this directory may be acceptable for low-value secrets that
won't be too damaging if leaked. High-value secrets that will cause damage if
leaked should not be stored here.

In the future we intend to provide recommended secure ways of dealing with
high-value secrets. This would involve not keeping secrets in the repository,
and storing them securely in AWS Secrets Manager instead. In the meantime,
until better instructions exist, please contact Metaplay for details if this
currently seems important to you.
