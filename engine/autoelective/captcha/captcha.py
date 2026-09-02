class Captcha(object):

    __slots__ = ['_code']

    def __init__(self, code):
        self._code = code

    @property
    def code(self):
        return self._code

    def __repr__(self):
        return '%s(%r)' % (
            self.__class__.__name__,
            self._code,
        )
