class Captcha(object):

    __slots__ = ['_code', '_confidence', '_engine']

    def __init__(self, code, confidence=None, engine=""):
        self._code = code
        self._confidence = confidence
        self._engine = engine

    @property
    def code(self):
        return self._code

    @property
    def confidence(self):
        return self._confidence

    @property
    def engine(self):
        return self._engine

    def __repr__(self):
        return '%s(%r)' % (
            self.__class__.__name__,
            self._code,
        )
